#pragma once

#include <windows.h>
#include <string>

inline constexpr CLSID TaskbarSiteId =
    {0x29c4822a, 0x17a4, 0x4bc1, {0xae, 0xc4, 0x19, 0x6b, 0x38, 0x63, 0x81, 0x2f}};

inline std::wstring SessionName(DWORD controllerPid, const wchar_t* name)
{
    return L"Local\\WindowsDock.NativeTaskbar." + std::to_wstring(controllerPid) + L"." + name;
}

inline std::wstring AppearanceName(DWORD controllerPid, unsigned attempt, const wchar_t* name)
{
    return SessionName(controllerPid, L"Appearance") + L"." + std::to_wstring(attempt) + L"." + name;
}

inline constexpr unsigned TestedWindowsBuild = 26200;
