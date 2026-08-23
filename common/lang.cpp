
#include "lang.h"
#include <commctrl.h>

static HMODULE g_hLangDll = NULL;
static std::wstring g_currentLang;

static const struct { const wchar_t* code; const wchar_t* name; } g_langNames[] = {
    { L"zh-cn", L"中文(简体)" },
    { L"zh-tw", L"中文(繁体)" },
    { L"en-us", L"English (US)" },
    { L"ja-jp", L"日本語" },
    { L"ru-ru", L"Русский" },
    { L"ko-kr", L"한국어" },
};

namespace Lang
{

static std::wstring GetExeDir()
{
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(NULL, exePath, MAX_PATH);
    std::wstring dir = exePath;
    size_t pos = dir.find_last_of(L"\\/");
    if (pos != std::wstring::npos) dir = dir.substr(0, pos);
    return dir;
}

bool Load(const std::wstring& progName, const std::wstring& langCode)
{
    Unload();
    std::wstring dllPath = GetExeDir() + L"\\language\\" + progName + L"_" + langCode + L".dll";
    g_hLangDll = LoadLibraryExW(dllPath.c_str(), NULL, LOAD_LIBRARY_AS_DATAFILE);
    if (!g_hLangDll) return false;
    g_currentLang = langCode;
    return true;
}

std::wstring GetStr(UINT id, const wchar_t* fallback)
{
    if (g_hLangDll)
    {
        wchar_t buf[2048] = {};
        int len = LoadStringW(g_hLangDll, id, buf, 2048);
        if (len > 0) return std::wstring(buf, len);
    }
    return fallback ? std::wstring(fallback) : L"";
}

std::vector<LanguageInfo> EnumAvailable(const std::wstring& progName)
{
    std::vector<LanguageInfo> result;
    std::wstring langDir = GetExeDir() + L"\\language";
    std::wstring search = langDir + L"\\" + progName + L"_*.dll";

    WIN32_FIND_DATAW fd;
    HANDLE hFind = FindFirstFileW(search.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE) return result;

    do
    {
        std::wstring fname = fd.cFileName;
        std::wstring prefix = progName + L"_";
        if (fname.rfind(prefix, 0) == 0)
        {
            std::wstring code = fname.substr(prefix.length());
            size_t dot = code.rfind(L".dll");
            if (dot != std::wstring::npos) code = code.substr(0, dot);
            LanguageInfo info;
            info.code = code;
            info.name = GetLanguageName(code);
            if (info.name.empty()) info.name = code;
            result.push_back(info);
        }
    } while (FindNextFileW(hFind, &fd));
    FindClose(hFind);
    return result;
}

std::wstring GetLanguageName(const std::wstring& code)
{
    for (auto& ln : g_langNames)
    {
        if (_wcsicmp(ln.code, code.c_str()) == 0) return ln.name;
    }
    return L"";
}

std::wstring GetCurrent() { return g_currentLang; }

void Unload()
{
    if (g_hLangDll) { FreeLibrary(g_hLangDll); g_hLangDll = NULL; }
    g_currentLang.clear();
}


#define IDC_LANGLIST  2001
#define IDC_LANGOK    2002
#define IDC_LANGCANCEL 2003

static HWND g_hLangDlg = NULL;
static std::vector<LanguageInfo> g_dlgLangs;
static std::wstring g_dlgResult;
static HFONT g_dlgFont = NULL;

static LRESULT CALLBACK LangDlgProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        g_dlgFont = (HFONT)GetStockObject(DEFAULT_GUI_FONT);
        NONCLIENTMETRICSW ncm = {};
        ncm.cbSize = sizeof(ncm);
        SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0);
        g_dlgFont = CreateFontIndirectW(&ncm.lfMessageFont);

        CreateWindowExW(0, L"STATIC", L"选择语言:", WS_CHILD | WS_VISIBLE,
            15, 12, 200, 20, hWnd, NULL, NULL, NULL);

        HWND hList = CreateWindowExW(WS_EX_CLIENTEDGE, L"LISTBOX", L"",
            WS_CHILD | WS_VISIBLE | WS_VSCROLL | LBS_NOTIFY,
            15, 35, 290, 180, hWnd, (HMENU)IDC_LANGLIST, NULL, NULL);
        SendMessageW(hList, WM_SETFONT, (WPARAM)g_dlgFont, TRUE);

        for (size_t i = 0; i < g_dlgLangs.size(); i++)
        {
            std::wstring display = g_dlgLangs[i].name + L"  (" + g_dlgLangs[i].code + L")";
            int idx = (int)SendMessageW(hList, LB_ADDSTRING, 0, (LPARAM)display.c_str());
            if (_wcsicmp(g_dlgLangs[i].code.c_str(), g_currentLang.c_str()) == 0)
                SendMessageW(hList, LB_SETCURSEL, idx, 0);
        }
        if (SendMessageW(hList, LB_GETCURSEL, 0, 0) == LB_ERR && g_dlgLangs.size() > 0)
            SendMessageW(hList, LB_SETCURSEL, 0, 0);

        CreateWindowExW(0, L"BUTTON", L"确定", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            120, 225, 80, 28, hWnd, (HMENU)IDC_LANGOK, NULL, NULL);
        CreateWindowExW(0, L"BUTTON", L"取消", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            210, 225, 80, 28, hWnd, (HMENU)IDC_LANGCANCEL, NULL, NULL);

        EnumChildWindows(hWnd, [](HWND h, LPARAM lp) -> BOOL {
            SendMessageW(h, WM_SETFONT, (WPARAM)lp, TRUE); return TRUE;
        }, (LPARAM)g_dlgFont);
        return 0;
    }
    case WM_COMMAND:
    {
        if (LOWORD(wParam) == IDC_LANGOK || (LOWORD(wParam) == IDC_LANGLIST && HIWORD(wParam) == LBN_DBLCLK))
        {
            HWND hList = GetDlgItem(hWnd, IDC_LANGLIST);
            int sel = (int)SendMessageW(hList, LB_GETCURSEL, 0, 0);
            if (sel != LB_ERR) g_dlgResult = g_dlgLangs[sel].code;
            DestroyWindow(hWnd);
            return 0;
        }
        if (LOWORD(wParam) == IDC_LANGCANCEL)
        {
            g_dlgResult.clear();
            DestroyWindow(hWnd);
            return 0;
        }
        break;
    }
    case WM_CLOSE:
        g_dlgResult.clear();
        DestroyWindow(hWnd);
        return 0;
    case WM_DESTROY:
        if (g_dlgFont) { DeleteObject(g_dlgFont); g_dlgFont = NULL; }
        return 0;
    }
    return DefWindowProcW(hWnd, msg, wParam, lParam);
}

std::wstring ShowDialog(HWND parent, const std::wstring& progName)
{
    g_dlgLangs = EnumAvailable(progName);
    if (g_dlgLangs.empty())
    {
        MessageBoxW(parent, L"未找到语言文件 (language 目录)", L"语言切换", MB_OK | MB_ICONINFORMATION);
        return L"";
    }
    g_dlgResult.clear();

    static bool registered = false;
    if (!registered)
    {
        WNDCLASSEXW wc = {};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = LangDlgProc;
        wc.hInstance = GetModuleHandleW(NULL);
        wc.hCursor = LoadCursor(NULL, IDC_ARROW);
        wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
        wc.lpszClassName = L"LangSelectDialog";
        RegisterClassExW(&wc);
        registered = true;
    }

    RECT rc = { 0, 0, 320, 270 };
    AdjustWindowRectEx(&rc, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU, FALSE, 0);
    int w = rc.right - rc.left, h = rc.bottom - rc.top;

    RECT prc;
    GetWindowRect(parent, &prc);
    int x = prc.left + (prc.right - prc.left - w) / 2;
    int y = prc.top + (prc.bottom - prc.top - h) / 2;

    HWND hDlg = CreateWindowExW(0, L"LangSelectDialog", L"选择语言",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME,
        x, y, w, h, parent, NULL, GetModuleHandleW(NULL), NULL);

    if (!hDlg) return L"";
    ShowWindow(hDlg, SW_SHOW);
    UpdateWindow(hDlg);

    MSG msg;
    while (IsWindow(hDlg) && GetMessageW(&msg, NULL, 0, 0))
    {
        if (!IsDialogMessageW(hDlg, &msg))
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    return g_dlgResult;
}

} 
