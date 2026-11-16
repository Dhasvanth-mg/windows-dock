#include "Connection.h"
#include "Log.h"

#include <ocidl.h>
#include <xamlom.h>
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
#include <winrt/base.h>
#undef GetCurrentTime
#include "TaskbarStyle.h"
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>

#include <atomic>
#include <cwchar>
#include <map>
#include <memory>
#include <mutex>
#include <thread>

namespace
{
    namespace media = xaml::Media;
    using winrt::Windows::System::DispatcherQueue;
    using xaml::Hosting::DesktopWindowXamlSource;

    struct DiagnosticString
    {
        BSTR value;
        ~DiagnosticString() { SysFreeString(value); }

        std::wstring_view View() const
        {
            return value ? std::wstring_view(value, SysStringLen(value)) : std::wstring_view{};
        }
    };

    class Watcher : public winrt::implements<Watcher, IVisualTreeServiceCallback2>
    {
    public:
        Watcher(winrt::com_ptr<IXamlDiagnostics> diagnostics, DWORD controllerPid, unsigned attempt) : diagnostics(std::move(diagnostics)),
            controllerPid(controllerPid),
            controller(OpenProcess(SYNCHRONIZE, FALSE, controllerPid)),
            ready(OpenEventW(EVENT_MODIFY_STATE, FALSE, AppearanceName(controllerPid, attempt, L"Ready").c_str())),
            restored(OpenEventW(EVENT_MODIFY_STATE, FALSE, AppearanceName(controllerPid, attempt, L"Restored").c_str())),
            blocked(OpenEventW(EVENT_MODIFY_STATE, FALSE, AppearanceName(controllerPid, attempt, L"Blocked").c_str())),
            stop(OpenEventW(SYNCHRONIZE, FALSE, AppearanceName(controllerPid, attempt, L"Stop").c_str())),
            dashboard(OpenEventW(EVENT_MODIFY_STATE, FALSE, SessionName(controllerPid, L"Dashboard").c_str()))
        {
            if (!controller || !ready || !restored || !blocked || !stop || !dashboard)
                winrt::throw_hresult(E_ACCESSDENIED);
        }

        void Run()
        {
            auto service = diagnostics.as<IVisualTreeService3>();
            // A partial subscription can still deliver the primary taskbar callbacks. Let the controller
            // time out the connection, and keep the stop path alive for any work it has already queued.
            const auto result = service->AdviseVisualTreeChange(this);
            countInitialElements = false;
            if (FAILED(result))
            {
                const auto message = L"The visual tree subscription was incomplete after " +
                    std::to_wstring(initialElements.load()) + L" element callbacks.";
                DockLog(L"explorer", message.c_str(), static_cast<unsigned long>(result));
            }

            const HANDLE signals[] = {stop.get(), controller.get()};
            WaitForMultipleObjects(ARRAYSIZE(signals), signals, FALSE, INFINITE);
            stopping = true;
            const auto unadvised = service->UnadviseVisualTreeChange(this);
            if (FAILED(unadvised))
                DockLog(L"explorer", L"The visual tree subscription did not detach.", static_cast<unsigned long>(unadvised));

            DispatcherQueue queue{nullptr};
            {
                std::scoped_lock guard(taskbarLock);
                queue = taskbarQueue;
            }
            if (!queue || !queue.TryEnqueue([self = get_strong()]
                {
                    self->style.reset();
                    SetEvent(self->restored.get());
                }))
                SetEvent(restored.get());
        }

        HRESULT __stdcall OnVisualTreeChange(ParentChildRelation, VisualElement element,
            VisualMutationType change) noexcept override
        {
            const DiagnosticString type{element.Type};
            const DiagnosticString name{element.Name};
            const DiagnosticString file{element.SrcInfo.FileName};
            const DiagnosticString hash{element.SrcInfo.Hash};
            if (change != Add || stopping)
                return S_OK;
            if (countInitialElements)
                ++initialElements;
            try
            {
                if (type.View() == L"Windows.UI.Xaml.Hosting.DesktopWindowXamlSource")
                    RememberSource(element.Handle);
                else if (type.View() == L"Taskbar.TaskbarFrame")
                    QueueStyle(element.Handle);
                else if (name.View() == L"RunningIndicator")
                    QueueIndicator(element.Handle);
            }
            catch (...) {}
            return S_OK;
        }

        HRESULT __stdcall OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override
        {
            return S_OK;
        }

    private:
        bool IsStopping() const
        {
            return stopping || WaitForSingleObject(stop.get(), 0) == WAIT_OBJECT_0 ||
                WaitForSingleObject(controller.get(), 0) == WAIT_OBJECT_0;
        }

        template <typename T>
        T Get(InstanceHandle handle)
        {
            winrt::Windows::Foundation::IInspectable object{nullptr};
            winrt::check_hresult(diagnostics->GetIInspectableFromHandle(handle,
                reinterpret_cast<::IInspectable**>(winrt::put_abi(object))));
            return object.try_as<T>();
        }

        void QueueStyle(InstanceHandle handle)
        {
            const auto frame = Get<xaml::FrameworkElement>(handle);
            if (!frame)
                return;
            auto root = media::VisualTreeHelper::GetParent(frame).try_as<controls::Grid>();
            const auto queue = DispatcherQueue::GetForCurrentThread();
            if (!root || !queue)
                return;
            const auto source = CurrentTaskbarSource(root);
            if (!source)
                return;
            QueueStyle(frame, root, queue);
        }

        void QueueStyle(xaml::FrameworkElement const& frame, controls::Grid const& root, DispatcherQueue const& queue)
        {
            queue.TryEnqueue([self = get_strong(), frame = winrt::make_weak(frame), root = winrt::make_weak(root)]
            {
                if (self->IsStopping())
                    return;
                const auto grid = root.get();
                const auto apps = frame.get();
                if (!grid || !apps || !self->CurrentTaskbarSource(grid) || self->styledRoot.get() == grid)
                    return;

                try
                {
                    xaml::FrameworkElement tray{nullptr};
                    for (const auto& child : grid.Children())
                        if (winrt::get_class_name(child) == L"SystemTray.SystemTrayFrame")
                            tray = child.try_as<xaml::FrameworkElement>();
                    if (!tray)
                    {
                        DockLog(L"explorer", L"Taskbar not styled: the system tray frame was not found.");
                        return;
                    }
                    if (TaskbarStyle::Find(apps, L"HitodamaLook"))
                    {
                        DockLog(L"explorer", L"Hitodama owns the primary taskbar appearance.");
                        SetEvent(self->blocked.get());
                        return;
                    }
                    if (TaskbarStyle::Find(grid, L"WindowsDockGlass"))
                    {
                        DockLog(L"explorer", L"The primary taskbar still has Windows Dock styling from another session.");
                        SetEvent(self->blocked.get());
                        return;
                    }

                    {
                        std::scoped_lock guard(self->taskbarLock);
                        if (self->IsStopping())
                            return;
                        self->taskbarQueue = DispatcherQueue::GetForCurrentThread();
                    }
                    self->style.reset();
                    auto next = std::make_unique<TaskbarStyle>(grid, self->dashboard, self->controllerPid);
                    next->Apply(apps, tray);
                    if (self->IsStopping())
                        return;
                    self->style = std::move(next);
                    self->styledRoot = winrt::make_weak(grid);
                    SetEvent(self->ready.get());
                    DockLog(L"explorer", L"Taskbar styled.");
                }
                catch (...)
                {
                    DockLog(L"explorer", L"Taskbar styling failed; the layout was not as expected.",
                        static_cast<unsigned long>(winrt::to_hresult().value));
                }
            });
        }

        void RememberSource(InstanceHandle handle)
        {
            auto source = Get<DesktopWindowXamlSource>(handle);
            if (!source)
                return;
            auto native = source.try_as<IDesktopWindowXamlSourceNative>();
            HWND window = nullptr;
            if (!native || FAILED(native->get_WindowHandle(&window)) || !window)
                return;
            if (GetAncestor(window, GA_ROOT) != FindWindowW(L"Shell_TrayWnd", nullptr))
                return;
            {
                std::scoped_lock guard(taskbarLock);
                primarySources[GetCurrentThreadId()] = winrt::make_weak(source);
            }
            auto root = source.Content().try_as<controls::Grid>();
            auto queue = DispatcherQueue::GetForCurrentThread();
            if (!root || !queue)
                return;
            for (auto const& child : root.Children())
            {
                if (winrt::get_class_name(child) == L"Taskbar.TaskbarFrame")
                {
                    auto frame = child.try_as<xaml::FrameworkElement>();
                    if (frame)
                        QueueStyle(frame, root, queue);
                    return;
                }
            }
        }

        DesktopWindowXamlSource CurrentTaskbarSource(controls::Grid const& grid)
        {
            std::scoped_lock guard(taskbarLock);
            const auto it = primarySources.find(GetCurrentThreadId());
            if (it == primarySources.end())
                return nullptr;
            const auto source = it->second.get();
            return source && source.Content() == grid ? source : nullptr;
        }

        void QueueIndicator(InstanceHandle handle)
        {
            auto queue = DispatcherQueue::GetForCurrentThread();
            {
                std::scoped_lock guard(taskbarLock);
                if (!taskbarQueue || taskbarQueue != queue)
                    return;
            }
            if (style)
            {
                auto indicator = Get<xaml::FrameworkElement>(handle);
                if (indicator)
                    queue.TryEnqueue([self = get_strong(), indicator = winrt::make_weak(indicator)]
                    {
                        try
                        {
                            if (auto element = indicator.get(); self->style && !self->stopping && element)
                                self->style->StyleIndicator(element);
                        }
                        catch (...) {}
                    });
            }
        }

        winrt::com_ptr<IXamlDiagnostics> diagnostics;
        DWORD controllerPid;
        winrt::handle controller, ready, restored, blocked, stop, dashboard;
        std::atomic<bool> stopping{};
        std::atomic<bool> countInitialElements{true};
        std::atomic<unsigned> initialElements{};
        std::mutex taskbarLock;
        DispatcherQueue taskbarQueue{nullptr};
        std::map<DWORD, winrt::weak_ref<DesktopWindowXamlSource>> primarySources;
        winrt::weak_ref<controls::Grid> styledRoot;
        std::unique_ptr<TaskbarStyle> style;
    };

    class Site : public winrt::implements<Site, IObjectWithSite>
    {
    public:
        HRESULT __stdcall SetSite(IUnknown* value) noexcept override
        {
            try
            {
                site.copy_from(value);
                if (!site)
                    return S_OK;

                auto diagnostics = site.as<IXamlDiagnostics>();
                DiagnosticString config{};
                winrt::check_hresult(diagnostics->GetInitializationData(&config.value));
                const std::wstring input(config.View());
                wchar_t* end = nullptr;
                const auto pid = std::wcstoul(input.c_str(), &end, 10);
                if (!pid || *end != L':')
                    return E_INVALIDARG;
                const auto attempt = std::wcstoul(end + 1, &end, 10);
                if (!attempt || *end != L'\0')
                    return E_INVALIDARG;

                auto watcher = winrt::make_self<Watcher>(diagnostics, pid, attempt);
                std::thread([watcher]() mutable
                {
                    const auto apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                    if (FAILED(apartment))
                    {
                        DockLog(L"explorer", L"Could not initialize the taskbar watcher thread.", static_cast<unsigned long>(apartment));
                        return;
                    }
                    try { watcher->Run(); }
                    catch (...)
                    {
                        DockLog(L"explorer", L"Taskbar watcher stopped.", static_cast<unsigned long>(winrt::to_hresult().value));
                    }
                    watcher = nullptr;
                    CoUninitialize();
                }).detach();
                return S_OK;
            }
            catch (...)
            {
                return winrt::to_hresult();
            }
        }

        HRESULT __stdcall GetSite(REFIID iid, void** value) noexcept override
        {
            if (!value)
                return E_POINTER;
            *value = nullptr;
            return site ? site->QueryInterface(iid, value) : E_FAIL;
        }

    private:
        winrt::com_ptr<IUnknown> site;
    };

    class Factory : public winrt::implements<Factory, IClassFactory>
    {
    public:
        HRESULT __stdcall CreateInstance(IUnknown* outer, REFIID iid, void** value) noexcept override
        {
            if (!value)
                return E_POINTER;
            *value = nullptr;
            if (outer)
                return CLASS_E_NOAGGREGATION;
            try
            {
                return winrt::make_self<Site>()->QueryInterface(iid, value);
            }
            catch (...)
            {
                return winrt::to_hresult();
            }
        }

        HRESULT __stdcall LockServer(BOOL) noexcept override { return S_OK; }
    };
}

extern "C" HRESULT __stdcall GetDiagnosticsClass(REFCLSID clsid, REFIID iid, void** value) noexcept
{
    if (!value)
        return E_POINTER;
    *value = nullptr;
    if (clsid != TaskbarSiteId)
        return CLASS_E_CLASSNOTAVAILABLE;
    try
    {
        HMODULE module = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&GetDiagnosticsClass), &module))
            return HRESULT_FROM_WIN32(GetLastError());
        return winrt::make_self<Factory>()->QueryInterface(iid, value);
    }
    catch (...)
    {
        return winrt::to_hresult();
    }
}

extern "C" HRESULT __stdcall CanUnloadDiagnostics() noexcept { return S_FALSE; }
