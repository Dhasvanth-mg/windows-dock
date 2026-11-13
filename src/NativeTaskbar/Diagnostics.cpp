#include "Connection.h"

#include <ocidl.h>
#include <xamlom.h>
#include <winrt/base.h>
#undef GetCurrentTime
#include "TaskbarStyle.h"
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>

#include <atomic>
#include <cwchar>
#include <memory>
#include <mutex>
#include <thread>

namespace
{
    namespace media = xaml::Media;
    using winrt::Windows::System::DispatcherQueue;

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
        Watcher(winrt::com_ptr<IXamlDiagnostics> diagnostics, DWORD controllerPid) : diagnostics(std::move(diagnostics)),
            controller(OpenProcess(SYNCHRONIZE, FALSE, controllerPid)),
            ready(OpenEventW(EVENT_MODIFY_STATE, FALSE, SessionName(controllerPid, L"Ready").c_str())),
            restored(OpenEventW(EVENT_MODIFY_STATE, FALSE, SessionName(controllerPid, L"Restored").c_str())),
            stop(OpenEventW(SYNCHRONIZE, FALSE, SessionName(controllerPid, L"Stop").c_str())),
            dashboard(OpenEventW(EVENT_MODIFY_STATE, FALSE, SessionName(controllerPid, L"Dashboard").c_str()))
        {
            if (!controller || !ready || !restored || !stop || !dashboard)
                winrt::throw_hresult(E_ACCESSDENIED);
        }

        void Run()
        {
            auto service = diagnostics.as<IVisualTreeService3>();
            if (FAILED(service->AdviseVisualTreeChange(this)))
            {
                SetEvent(restored.get());
                return;
            }

            const HANDLE signals[] = {stop.get(), controller.get()};
            WaitForMultipleObjects(ARRAYSIZE(signals), signals, FALSE, INFINITE);
            stopping = true;

            DispatcherQueue queue{nullptr};
            {
                std::scoped_lock guard(taskbarLock);
                queue = taskbarQueue;
            }
            if (queue)
            {
                queue.TryEnqueue([self = get_strong()]
                {
                    self->style.reset();
                    SetEvent(self->restored.get());
                });
            }
            else
            {
                SetEvent(restored.get());
            }
            service->UnadviseVisualTreeChange(this);
        }

        HRESULT __stdcall OnVisualTreeChange(ParentChildRelation, VisualElement element,
            VisualMutationType change) noexcept override
        {
            if (change != Add)
                return S_OK;

            const DiagnosticString type{element.Type};
            const DiagnosticString name{element.Name};
            const DiagnosticString file{element.SrcInfo.FileName};
            const DiagnosticString hash{element.SrcInfo.Hash};
            if (stopping)
                return S_OK;
            try
            {
                if (type.View() == L"Taskbar.TaskbarFrame")
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

            queue.TryEnqueue([self = get_strong(), frame = winrt::make_weak(frame), root = winrt::make_weak(root)]
            {
                if (self->stopping || self->style)
                    return;
                const auto grid = root.get();
                const auto apps = frame.get();
                if (!grid || !apps)
                    return;

                try
                {
                    xaml::FrameworkElement tray{nullptr};
                    for (const auto& child : grid.Children())
                        if (winrt::get_class_name(child) == L"SystemTray.SystemTrayFrame")
                            tray = child.try_as<xaml::FrameworkElement>();
                    if (!tray || TaskbarStyle::Find(apps, L"HitodamaLook"))
                        return;

                    auto next = std::make_unique<TaskbarStyle>(grid, self->dashboard);
                    next->Apply(apps, tray);
                    {
                        std::scoped_lock guard(self->taskbarLock);
                        self->taskbarQueue = DispatcherQueue::GetForCurrentThread();
                        self->style = std::move(next);
                    }
                    SetEvent(self->ready.get());
                }
                catch (...) {}
            });
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
        winrt::handle controller, ready, restored, stop, dashboard;
        std::atomic<bool> stopping{};
        std::mutex taskbarLock;
        DispatcherQueue taskbarQueue{nullptr};
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
                if (!pid || *end != L'\0')
                    return E_INVALIDARG;

                auto watcher = winrt::make_self<Watcher>(diagnostics, pid);
                std::thread([watcher]
                {
                    try { watcher->Run(); } catch (...) {}
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
