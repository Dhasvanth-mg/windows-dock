#pragma once

#include <windows.h>
#include <cstdio>
#include <string>

// Appends one line to %LOCALAPPDATA%\WindowsDock\Logs\native.log. It is shared by the controller and the module
// that runs inside Explorer, so it takes no locks and never throws. Log what failed, never user content.
inline void DockLog(const wchar_t* area, const wchar_t* message, unsigned long code = 0) noexcept
{
    try
    {
        wchar_t base[MAX_PATH];
        const auto length = GetEnvironmentVariableW(L"LOCALAPPDATA", base, ARRAYSIZE(base));
        if (!length || length >= ARRAYSIZE(base))
            return;
        std::wstring directory = std::wstring(base) + L"\\WindowsDock";
        CreateDirectoryW(directory.c_str(), nullptr);
        directory += L"\\Logs";
        CreateDirectoryW(directory.c_str(), nullptr);
        const auto path = directory + L"\\native.log";

        WIN32_FILE_ATTRIBUTE_DATA info{};
        if (GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &info) && info.nFileSizeHigh == 0 &&
            info.nFileSizeLow > 512 * 1024)
            MoveFileExW(path.c_str(), (path + L".1").c_str(), MOVEFILE_REPLACE_EXISTING);

        SYSTEMTIME now;
        GetSystemTime(&now);
        wchar_t line[600];
        swprintf_s(line, L"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ [%lu] %ls: %ls", now.wYear, now.wMonth, now.wDay,
            now.wHour, now.wMinute, now.wSecond, now.wMilliseconds, GetCurrentProcessId(), area, message);
        std::wstring text(line);
        if (code)
        {
            wchar_t suffix[40];
            swprintf_s(suffix, L" | 0x%08lX", code);
            text += suffix;
        }
        text += L"\r\n";
        const auto size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
        if (size <= 0)
            return;
        std::string bytes(static_cast<size_t>(size), '\0');
        WideCharToMultiByte(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), bytes.data(), size, nullptr, nullptr);
        const auto file = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
            return;
        DWORD written = 0;
        WriteFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr);
        CloseHandle(file);
    }
    catch (...) {}
}
