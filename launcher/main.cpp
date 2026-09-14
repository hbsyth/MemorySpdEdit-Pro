// Native bootstrap: check .NET 10 Desktop Runtime, then run embedded app payload.
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <Shellapi.h>
#include <ShlObj.h>
#include <stdio.h>

#include "resource.h"

#pragma comment(lib, "Shell32.lib")
#pragma comment(lib, "Ole32.lib")

namespace {

// UTF-16 literals via \u to avoid MSVC source code-page issues.
constexpr wchar_t kAppTitle[] = L"MemorySpdEdit Pro";
constexpr wchar_t kDownloadUrl[] =
    L"https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";
constexpr wchar_t kDownloadPage[] = L"https://dotnet.microsoft.com/download/dotnet/10.0";

bool DirHasVersion10(const wchar_t* sharedFxDir) {
    wchar_t pattern[MAX_PATH];
    swprintf_s(pattern, L"%s\\10.*", sharedFxDir);
    WIN32_FIND_DATAW fd{};
    HANDLE h = FindFirstFileW(pattern, &fd);
    if (h == INVALID_HANDLE_VALUE) return false;
    bool ok = false;
    do {
        if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && fd.cFileName[0] != L'.') {
            ok = true;
            break;
        }
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return ok;
}

bool AppendSharedFx(wchar_t* buf, size_t cch, const wchar_t* root, const wchar_t* fxName) {
    return swprintf_s(buf, cch, L"%s\\dotnet\\shared\\%s", root, fxName) > 0;
}

bool HasDotNet10Desktop() {
    wchar_t base[MAX_PATH]{};
    wchar_t fx[MAX_PATH]{};

    DWORD n = GetEnvironmentVariableW(L"DOTNET_ROOT", base, MAX_PATH);
    if (n > 0 && n < MAX_PATH) {
        swprintf_s(fx, L"%s\\shared\\Microsoft.WindowsDesktop.App", base);
        if (DirHasVersion10(fx)) return true;
    }

    wchar_t pf[MAX_PATH]{};
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_PROGRAM_FILES, nullptr, 0, pf))) {
        if (AppendSharedFx(fx, MAX_PATH, pf, L"Microsoft.WindowsDesktop.App") && DirHasVersion10(fx))
            return true;
    }

    n = GetEnvironmentVariableW(L"ProgramW6432", base, MAX_PATH);
    if (n > 0 && n < MAX_PATH) {
        if (AppendSharedFx(fx, MAX_PATH, base, L"Microsoft.WindowsDesktop.App") && DirHasVersion10(fx))
            return true;
    }

    return false;
}

void PromptInstallRuntime() {
    // 本机未检测到 .NET 10 桌面运行时（Desktop Runtime x64）。\n\n请先安装后再启动本程序。\n\n点击「是」打开官方安装包下载；点击「否」打开下载说明页。
    const wchar_t* msg =
        L"\u672c\u673a\u672a\u68c0\u6d4b\u5230 .NET 10 \u684c\u9762\u8fd0\u884c\u65f6\uff08Desktop Runtime x64\uff09\u3002\n\n"
        L"\u8bf7\u5148\u5b89\u88c5\u540e\u518d\u542f\u52a8\u672c\u7a0b\u5e8f\u3002\n\n"
        L"\u70b9\u51fb\u300c\u662f\u300d\u6253\u5f00\u5b98\u65b9\u5b89\u88c5\u5305\u4e0b\u8f7d\uff1b\u70b9\u51fb\u300c\u5426\u300d\u6253\u5f00\u4e0b\u8f7d\u8bf4\u660e\u9875\u3002";
    int r = MessageBoxW(nullptr, msg, kAppTitle, MB_ICONWARNING | MB_YESNOCANCEL | MB_DEFBUTTON1);
    if (r == IDYES) {
        ShellExecuteW(nullptr, L"open", kDownloadUrl, nullptr, nullptr, SW_SHOWNORMAL);
    } else if (r == IDNO) {
        ShellExecuteW(nullptr, L"open", kDownloadPage, nullptr, nullptr, SW_SHOWNORMAL);
    }
}

bool GetPayloadDir(wchar_t* out, size_t cch) {
    wchar_t local[MAX_PATH]{};
    if (FAILED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, local)))
        return false;
    swprintf_s(out, cch, L"%s\\MemorySpdEdit-Pro", local);
    CreateDirectoryW(out, nullptr);
    return true;
}

bool ExtractPayload(const wchar_t* targetPath) {
    HRSRC res = FindResourceW(nullptr, MAKEINTRESOURCEW(IDR_APP_PAYLOAD), RT_RCDATA);
    if (!res) return false;
    HGLOBAL h = LoadResource(nullptr, res);
    if (!h) return false;
    const void* data = LockResource(h);
    DWORD size = SizeofResource(nullptr, res);
    if (!data || size == 0) return false;

    WIN32_FILE_ATTRIBUTE_DATA attr{};
    if (GetFileAttributesExW(targetPath, GetFileExInfoStandard, &attr)) {
        ULARGE_INTEGER existing{};
        existing.LowPart = attr.nFileSizeLow;
        existing.HighPart = attr.nFileSizeHigh;
        if (existing.QuadPart == size) return true;
    }

    wchar_t tmpPath[MAX_PATH]{};
    swprintf_s(tmpPath, L"%s.tmp", targetPath);
    HANDLE file = CreateFileW(tmpPath, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    BOOL ok = WriteFile(file, data, size, &written, nullptr);
    CloseHandle(file);
    if (!ok || written != size) {
        DeleteFileW(tmpPath);
        return false;
    }
    MoveFileExW(tmpPath, targetPath, MOVEFILE_REPLACE_EXISTING);
    return true;
}

bool LaunchPayload(const wchar_t* path) {
    STARTUPINFOW si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};
    wchar_t cmd[MAX_PATH + 8]{};
    swprintf_s(cmd, L"\"%s\"", path);
    if (!CreateProcessW(path, cmd, nullptr, nullptr, FALSE, 0, nullptr, nullptr, &si, &pi))
        return false;
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return true;
}

}  // namespace

int APIENTRY wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int) {
    if (!HasDotNet10Desktop()) {
        PromptInstallRuntime();
        return 1;
    }

    wchar_t dir[MAX_PATH]{};
    wchar_t target[MAX_PATH]{};
    if (!GetPayloadDir(dir, MAX_PATH)) {
        MessageBoxW(nullptr,
                    L"\u65e0\u6cd5\u521b\u5efa\u672c\u5730\u7f13\u5b58\u76ee\u5f55\u3002",
                    kAppTitle, MB_ICONERROR | MB_OK);
        return 2;
    }
    swprintf_s(target, L"%s\\MemorySpdEdit-Pro.app.exe", dir);
    if (!ExtractPayload(target)) {
        MessageBoxW(nullptr,
                    L"\u91ca\u653e\u7a0b\u5e8f\u4e3b\u4f53\u5931\u8d25\uff0c\u8bf7\u91cd\u65b0\u4e0b\u8f7d\u5b89\u88c5\u5305\u3002",
                    kAppTitle, MB_ICONERROR | MB_OK);
        return 3;
    }
    if (!LaunchPayload(target)) {
        MessageBoxW(nullptr, L"\u542f\u52a8\u7a0b\u5e8f\u5931\u8d25\u3002", kAppTitle, MB_ICONERROR | MB_OK);
        return 4;
    }
    return 0;
}
