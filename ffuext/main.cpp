

#include <windows.h>
#include <commctrl.h>
#include <commdlg.h>
#include <string>
#include <vector>
#include <sstream>
#include <process.h>

#include "dism.h"
#include "disk.h"
#include "../common/lang.h"
#include "strings.h"

static const wchar_t* PROG_NAME = L"ffuext";

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "comdlg32.lib")

#define IDC_FFUEDIT     1001
#define IDC_BROWSEBTN   1002
#define IDC_DISKCOMBO   1003
#define IDC_REFRESHBTN  1004
#define IDC_STARTBTN    1005
#define IDC_PROGRESS    1006
#define IDC_OUTPUT      1007
#define IDC_STATUS      1008

#define WM_DISM_OUTPUT  (WM_USER + 100)
#define WM_DISM_DONE    (WM_USER + 101)

static HINSTANCE g_hInst = NULL;
static HWND g_hWnd = NULL;
static HWND g_hFFUEdit = NULL;
static HWND g_hBrowseBtn = NULL;
static HWND g_hDiskCombo = NULL;
static HWND g_hRefreshBtn = NULL;
static HWND g_hStartBtn = NULL;
static HWND g_hProgress = NULL;
static HWND g_hOutput = NULL;
static HWND g_hStatus = NULL;
static HMENU g_hMenu = NULL;
static HFONT g_hFont = NULL;
static HWND g_lblFFU = NULL;
static HWND g_lblDisk = NULL;
static HWND g_lblOutput = NULL;

static bool g_isRunning = false;
static HANDLE g_hWorkerThread = NULL;

struct ApplyParams
{
    std::wstring ffuPath;
    int          driveIndex;
};

struct ApplyResultMsg
{
    bool         success;
    DWORD        exitCode;
    std::wstring errorMessage;
};

static bool IsProcessElevated()
{
    HANDLE hToken = NULL;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &hToken))
        return false;

    TOKEN_ELEVATION elevation = {};
    DWORD cbSize = sizeof(elevation);
    bool result = false;
    if (GetTokenInformation(hToken, TokenElevation, &elevation, sizeof(elevation), &cbSize))
    {
        result = (elevation.TokenIsElevated != 0);
    }
    CloseHandle(hToken);
    return result;
}

static void AppendOutput(const std::wstring& text)
{
    if (!g_hOutput) return;
    int len = GetWindowTextLengthW(g_hOutput);
    SendMessageW(g_hOutput, EM_SETSEL, (WPARAM)len, (LPARAM)len);
    SendMessageW(g_hOutput, EM_REPLACESEL, FALSE, (LPARAM)text.c_str());
    
    SendMessageW(g_hOutput, EM_SCROLLCARET, 0, 0);
}

static void AppendOutputLine(const std::wstring& line)
{
    AppendOutput(line + L"\r\n");
}

static void SetStatus(const std::wstring& text)
{
    if (g_hStatus)
        SetWindowTextW(g_hStatus, text.c_str());
}

static void SetControlsEnabled(bool enabled)
{
    EnableWindow(g_hFFUEdit, enabled);
    EnableWindow(g_hBrowseBtn, enabled);
    EnableWindow(g_hDiskCombo, enabled);
    EnableWindow(g_hRefreshBtn, enabled);
    EnableWindow(g_hStartBtn, enabled);
}

static void RefreshDiskList()
{
    SendMessageW(g_hDiskCombo, CB_RESETCONTENT, 0, 0);

    std::vector<DiskInfo> disks = EnumeratePhysicalDisks();

    if (disks.empty())
    {
        SendMessageW(g_hDiskCombo, CB_ADDSTRING, 0, (LPARAM)L"(未检测到物理磁盘)");
        EnableWindow(g_hStartBtn, FALSE);
        return;
    }

    for (const auto& disk : disks)
    {
        std::wstringstream ss;
        ss << L"PhysicalDrive" << disk.index;
        if (disk.sizeBytes > 0)
            ss << L"  (" << FormatDiskSize(disk.sizeBytes) << L")";
        else
            ss << L"  (大小未知)";

        if (!disk.model.empty())
            ss << L"  " << disk.model;

        if (!disk.accessible)
            ss << L"  [需管理员权限]";

        int idx = (int)SendMessageW(g_hDiskCombo, CB_ADDSTRING, 0, (LPARAM)ss.str().c_str());
        
        SendMessageW(g_hDiskCombo, CB_SETITEMDATA, idx, (LPARAM)disk.index);
    }

    SendMessageW(g_hDiskCombo, CB_SETCURSEL, 0, 0);
    EnableWindow(g_hStartBtn, TRUE);
}

static void BrowseFFUFile()
{
    wchar_t szFile[MAX_PATH] = L"";

    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = g_hWnd;
    ofn.lpstrFilter = L"FFU 镜像文件 (*.ffu)\0*.ffu\0所有文件 (*.*)\0*.*\0";
    ofn.nFilterIndex = 1;
    ofn.lpstrFile = szFile;
    ofn.nMaxFile = MAX_PATH;
    ofn.lpstrTitle = L"选择 FFU 镜像文件";
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY;

    if (GetOpenFileNameW(&ofn))
    {
        SetWindowTextW(g_hFFUEdit, szFile);
    }
}

static void OnDismOutput(const std::wstring& line, int progress, void* userData)
{
    
    std::wstring* pLine = new std::wstring(line);
    PostMessageW(g_hWnd, WM_DISM_OUTPUT, (WPARAM)pLine, (LPARAM)progress);
}

static DWORD WINAPI WorkerThread(LPVOID param)
{
    ApplyParams* p = (ApplyParams*)param;

    DismApplyResult result = ApplyFfu(
        p->ffuPath,
        p->driveIndex,
        OnDismOutput,
        NULL);

    ApplyResultMsg* pMsg = new ApplyResultMsg();
    pMsg->success = result.success;
    pMsg->exitCode = result.exitCode;
    pMsg->errorMessage = result.errorMessage;
    PostMessageW(g_hWnd, WM_DISM_DONE, 0, (LPARAM)pMsg);

    delete p;
    return 0;
}

static void StartApply()
{
    if (g_isRunning) return;

    wchar_t ffuPath[MAX_PATH] = L"";
    GetWindowTextW(g_hFFUEdit, ffuPath, MAX_PATH);
    if (ffuPath[0] == L'\0')
    {
        MessageBoxW(g_hWnd, L"请先选择 FFU 文件", L"提示", MB_OK | MB_ICONWARNING);
        return;
    }

    DWORD attr = GetFileAttributesW(ffuPath);
    if (attr == INVALID_FILE_ATTRIBUTES || (attr & FILE_ATTRIBUTE_DIRECTORY))
    {
        MessageBoxW(g_hWnd, L"FFU 文件不存在", L"错误", MB_OK | MB_ICONERROR);
        return;
    }

    int sel = (int)SendMessageW(g_hDiskCombo, CB_GETCURSEL, 0, 0);
    if (sel == CB_ERR)
    {
        MessageBoxW(g_hWnd, L"请选择目标磁盘", L"提示", MB_OK | MB_ICONWARNING);
        return;
    }
    int driveIndex = (int)SendMessageW(g_hDiskCombo, CB_GETITEMDATA, sel, 0);
    if (driveIndex == CB_ERR)
    {
        MessageBoxW(g_hWnd, L"无效的磁盘选择", L"错误", MB_OK | MB_ICONERROR);
        return;
    }

    std::wstringstream warn;
    warn << L"即将把 FFU 镜像释放到 PhysicalDrive" << driveIndex << L"\r\n\r\n";
    warn << L"目标磁盘上的所有数据将被永久覆盖!\r\n\r\n";
    warn << L"是否继续?";
    if (MessageBoxW(g_hWnd, warn.str().c_str(), L"确认操作", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES)
    {
        return;
    }

    if (!IsProcessElevated())
    {
        MessageBoxW(g_hWnd,
            L"释放 FFU 到物理磁盘需要管理员权限。\r\n请右键以管理员身份重新运行本程序。",
            L"权限不足", MB_OK | MB_ICONERROR);
        return;
    }

    SetWindowTextW(g_hOutput, L"");
    SendMessageW(g_hProgress, PBM_SETPOS, 0, 0);

    g_isRunning = true;
    SetControlsEnabled(false);
    SetStatus(L"正在释放 FFU 镜像...");

    AppendOutputLine(L"=== FFU 释放开始 ===");
    AppendOutputLine(L"FFU 文件: " + std::wstring(ffuPath));
    AppendOutputLine(L"目标磁盘: PhysicalDrive" + std::to_wstring(driveIndex));
    AppendOutputLine(L"");

    ApplyParams* p = new ApplyParams();
    p->ffuPath = ffuPath;
    p->driveIndex = driveIndex;

    g_hWorkerThread = CreateThread(NULL, 0, WorkerThread, p, 0, NULL);
    if (!g_hWorkerThread)
    {
        delete p;
        g_isRunning = false;
        SetControlsEnabled(true);
        SetStatus(L"就绪");
        MessageBoxW(g_hWnd, L"无法创建工作线程", L"错误", MB_OK | MB_ICONERROR);
    }
}

static void ApplyLanguage()
{
    if (!g_hWnd) return;
    SetWindowTextW(g_hWnd, Lang::GetStr(IDS_APP_TITLE, L"ffuext").c_str());
    if (g_lblFFU) SetWindowTextW(g_lblFFU, Lang::GetStr(IDS_LBL_FFU, L"FFU 文件:").c_str());
    if (g_hBrowseBtn) SetWindowTextW(g_hBrowseBtn, Lang::GetStr(IDS_BTN_BROWSE, L"浏览...").c_str());
    if (g_lblDisk) SetWindowTextW(g_lblDisk, Lang::GetStr(IDS_LBL_DISK, L"目标磁盘:").c_str());
    if (g_hRefreshBtn) SetWindowTextW(g_hRefreshBtn, Lang::GetStr(IDS_BTN_REFRESH, L"刷新").c_str());
    if (g_hStartBtn) SetWindowTextW(g_hStartBtn, Lang::GetStr(IDS_BTN_START, L"开始释放").c_str());
    if (g_lblOutput) SetWindowTextW(g_lblOutput, Lang::GetStr(IDS_LBL_OUTPUT, L"输出日志:").c_str());
    if (!g_isRunning && g_hStatus) SetWindowTextW(g_hStatus, Lang::GetStr(IDS_STATUS_READY, L"就绪").c_str());

    if (g_hMenu)
    {
        HMENU hLang = GetSubMenu(g_hMenu, 0);
        if (hLang) ModifyMenuW(hLang, 0, MF_BYPOSITION | MF_STRING, 3001, Lang::GetStr(IDS_MENU_SWITCH, L"切换语言...").c_str());
        ModifyMenuW(g_hMenu, 0, MF_BYPOSITION | MF_STRING | MF_POPUP, (UINT_PTR)hLang, Lang::GetStr(IDS_MENU_LANG, L"语言").c_str());
        HMENU hHelp = GetSubMenu(g_hMenu, 1);
        if (hHelp) ModifyMenuW(hHelp, 0, MF_BYPOSITION | MF_STRING, 3002, Lang::GetStr(IDS_MENU_ABOUT, L"关于...").c_str());
        ModifyMenuW(g_hMenu, 1, MF_BYPOSITION | MF_STRING | MF_POPUP, (UINT_PTR)hHelp, Lang::GetStr(IDS_MENU_HELP, L"帮助").c_str());
    }
}

static LRESULT CALLBACK AboutDlgProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        NONCLIENTMETRICSW ncm = {}; ncm.cbSize = sizeof(ncm);
        SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0);
        HFONT hFont = CreateFontIndirectW(&ncm.lfMessageFont);
        CreateWindowExW(0, L"STATIC", L"ffuext v1.0.0.0", WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 15, 280, 24, hWnd, NULL, NULL, NULL);
        CreateWindowExW(0, L"STATIC", L"WinStory 2026", WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 45, 280, 20, hWnd, NULL, NULL, NULL);
        HWND hLink = CreateWindowExW(0, L"STATIC", L"https://wiki.win-story.cn", WS_CHILD | WS_VISIBLE | SS_CENTER | SS_NOTIFY, 10, 68, 280, 20, hWnd, (HMENU)4001, NULL, NULL);
        CreateWindowExW(0, L"STATIC", L"Compiled by DF4D3110", WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 95, 280, 20, hWnd, NULL, NULL, NULL);
        CreateWindowExW(0, L"BUTTON", L"OK", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON | BS_DEFPUSHBUTTON, 110, 130, 80, 28, hWnd, (HMENU)IDOK, NULL, NULL);
        EnumChildWindows(hWnd, [](HWND h, LPARAM lp) -> BOOL { SendMessageW(h, WM_SETFONT, (WPARAM)lp, TRUE); return TRUE; }, (LPARAM)hFont);
        SetWindowTextW(hLink, L"https://wiki.win-story.cn");
        return 0;
    }
    case WM_CTLCOLORSTATIC:
        if (GetDlgCtrlID((HWND)lParam) == 4001) { SetTextColor((HDC)wParam, RGB(0,0,255)); SetBkMode((HDC)wParam, TRANSPARENT); return (LRESULT)GetStockObject(NULL_BRUSH); }
        break;
    case WM_SETCURSOR:
        if (LOWORD(lParam) == HTCLIENT) { POINT pt; GetCursorPos(&pt); ScreenToClient(hWnd, &pt); RECT rc; GetWindowRect(GetDlgItem(hWnd,4001), &rc); ScreenToClient(hWnd,(LPPOINT)&rc); if (PtInRect(&rc,pt)) { SetCursor(LoadCursor(NULL,IDC_HAND)); return TRUE; } }
        break;
    case WM_COMMAND:
        if (LOWORD(wParam) == 4001) { ShellExecuteW(NULL, L"open", L"https://wiki.win-story.cn", NULL, NULL, SW_SHOWNORMAL); return 0; }
        if (LOWORD(wParam) == IDOK || LOWORD(wParam) == IDCANCEL) { DestroyWindow(hWnd); return 0; }
        break;
    case WM_CLOSE: DestroyWindow(hWnd); return 0;
    }
    return DefWindowProcW(hWnd, msg, wParam, lParam);
}

static void ShowAboutDialog(HWND parent)
{
    static bool reg = false;
    if (!reg) { WNDCLASSEXW wc = {}; wc.cbSize=sizeof(wc); wc.lpfnWndProc=AboutDlgProc; wc.hInstance=GetModuleHandleW(NULL); wc.hCursor=LoadCursor(NULL,IDC_ARROW); wc.hbrBackground=(HBRUSH)(COLOR_BTNFACE+1); wc.lpszClassName=L"AboutDlgFFU"; RegisterClassExW(&wc); reg=true; }
    RECT rc={0,0,300,180}; AdjustWindowRectEx(&rc,WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|DS_MODALFRAME,FALSE,0);
    int w=rc.right-rc.left,h=rc.bottom-rc.top; RECT prc; GetWindowRect(parent,&prc);
    HWND d=CreateWindowExW(0,L"AboutDlgFFU",L"关于 ffuext",WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|DS_MODALFRAME,prc.left+(prc.right-prc.left-w)/2,prc.top+(prc.bottom-prc.top-h)/2,w,h,parent,NULL,GetModuleHandleW(NULL),NULL);
    if(!d)return; ShowWindow(d,SW_SHOW); UpdateWindow(d);
    MSG m; while(IsWindow(d)&&GetMessageW(&m,NULL,0,0)){ if(!IsDialogMessageW(d,&m)){TranslateMessage(&m);DispatchMessageW(&m);} }
}

static void CreateControls(HWND hWnd)
{
    
    g_hMenu = CreateMenu();
    HMENU hLangMenu = CreatePopupMenu();
    AppendMenuW(hLangMenu, MF_STRING, 3001, L"切换语言...");
    AppendMenuW(g_hMenu, MF_POPUP, (UINT_PTR)hLangMenu, L"语言");
    HMENU hHelpMenu = CreatePopupMenu();
    AppendMenuW(hHelpMenu, MF_STRING, 3002, L"关于...");
    AppendMenuW(g_hMenu, MF_POPUP, (UINT_PTR)hHelpMenu, L"帮助");
    SetMenu(hWnd, g_hMenu);

    HFONT hFont = (HFONT)GetStockObject(DEFAULT_GUI_FONT);
    g_hFont = hFont;

    g_lblFFU = CreateWindowW(L"STATIC", L"FFU 文件:", WS_CHILD | WS_VISIBLE,
        12, 15, 60, 20, hWnd, NULL, g_hInst, NULL);

    g_hFFUEdit = CreateWindowW(L"EDIT", L"",
        WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL | ES_READONLY,
        78, 12, 480, 24, hWnd, (HMENU)IDC_FFUEDIT, g_hInst, NULL);

    g_hBrowseBtn = CreateWindowW(L"BUTTON", L"浏览...",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
        565, 11, 80, 26, hWnd, (HMENU)IDC_BROWSEBTN, g_hInst, NULL);

    g_lblDisk = CreateWindowW(L"STATIC", L"目标磁盘:", WS_CHILD | WS_VISIBLE,
        12, 48, 60, 20, hWnd, NULL, g_hInst, NULL);

    g_hDiskCombo = CreateWindowW(L"COMBOBOX", L"",
        WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST | WS_VSCROLL,
        78, 45, 480, 200, hWnd, (HMENU)IDC_DISKCOMBO, g_hInst, NULL);

    g_hRefreshBtn = CreateWindowW(L"BUTTON", L"刷新",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
        565, 44, 80, 26, hWnd, (HMENU)IDC_REFRESHBTN, g_hInst, NULL);

    g_hStartBtn = CreateWindowW(L"BUTTON", L"开始释放",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
        545, 80, 100, 30, hWnd, (HMENU)IDC_STARTBTN, g_hInst, NULL);

    g_hProgress = CreateWindowExW(0, PROGRESS_CLASSW, L"",
        WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
        12, 122, 636, 18, hWnd, (HMENU)IDC_PROGRESS, g_hInst, NULL);
    SendMessageW(g_hProgress, PBM_SETRANGE, 0, MAKELPARAM(0, 100));

    g_lblOutput = CreateWindowW(L"STATIC", L"输出日志:", WS_CHILD | WS_VISIBLE,
        12, 148, 60, 20, hWnd, NULL, g_hInst, NULL);

    g_hOutput = CreateWindowW(L"EDIT", L"",
        WS_CHILD | WS_VISIBLE | WS_BORDER | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
        12, 170, 636, 270, hWnd, (HMENU)IDC_OUTPUT, g_hInst, NULL);

    g_hStatus = CreateWindowW(L"STATIC", L"就绪",
        WS_CHILD | WS_VISIBLE | SS_LEFT,
        12, 448, 636, 20, hWnd, (HMENU)IDC_STATUS, g_hInst, NULL);

    EnumChildWindows(hWnd, [](HWND hChild, LPARAM lParam) -> BOOL {
        SendMessageW(hChild, WM_SETFONT, (WPARAM)lParam, TRUE);
        return TRUE;
    }, (LPARAM)hFont);
}

static LRESULT CALLBACK WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        CreateControls(hWnd);

        if (!Lang::Load(PROG_NAME, L"zh-cn"))
        {
            wchar_t langCode[32] = {};
            GetUserDefaultLocaleName(langCode, 32);
            Lang::Load(PROG_NAME, langCode);
        }
        ApplyLanguage();

        AppendOutputLine(L"=== ffuext - FFU 镜像释放工具 ===");

#if defined(_M_X64)
        AppendOutputLine(L"架构: amd64");
#elif defined(_M_IX86)
        AppendOutputLine(L"架构: x86");
#elif defined(_M_ARM64)
        AppendOutputLine(L"架构: arm64");
#elif defined(_M_ARM)
        AppendOutputLine(L"架构: arm32");
#endif

        if (IsProcessElevated())
        {
            AppendOutputLine(L"权限: 已提升 (管理员)");
        }
        else
        {
            AppendOutputLine(L"权限: 未提升 (释放 FFU 需要管理员身份运行)");
        }

        AppendOutputLine(L"正在检测 DISM /Apply-Ffu 支持...");
        bool ffuSupported = CheckDismFfuSupport();
        std::wstring dismVer = GetDismVersion();

        if (ffuSupported)
        {
            AppendOutputLine(L"DISM 版本: " + dismVer);
            AppendOutputLine(L"DISM /Apply-Ffu: 支持");
            SetStatus(L"就绪 - DISM 支持 FFU 释放");
        }
        else
        {
            AppendOutputLine(L"DISM 版本: " + dismVer);
            AppendOutputLine(L"DISM /Apply-Ffu: 不支持!");
            AppendOutputLine(L"提示: /Apply-Ffu 需要 Windows 10 1709+ 或 Windows 11 的 DISM 版本");
            SetStatus(L"警告 - 当前 DISM 不支持 FFU 释放");
            EnableWindow(g_hStartBtn, FALSE);
        }

        AppendOutputLine(L"");

        RefreshDiskList();
        AppendOutputLine(L"已枚举 " + std::to_wstring(SendMessageW(g_hDiskCombo, CB_GETCOUNT, 0, 0)) + L" 个物理磁盘");
        AppendOutputLine(L"");

        return 0;
    }

    case WM_COMMAND:
    {
        WORD id = LOWORD(wParam);
        WORD code = HIWORD(wParam);

        if (code == BN_CLICKED)
        {
            switch (id)
            {
            case 3001: 
            {
                std::wstring newLang = Lang::ShowDialog(g_hWnd, PROG_NAME);
                if (!newLang.empty() && newLang != Lang::GetCurrent())
                {
                    if (Lang::Load(PROG_NAME, newLang)) ApplyLanguage();
                }
                return 0;
            }
            case 3002: 
                ShowAboutDialog(g_hWnd);
                return 0;
            case IDC_BROWSEBTN:
                BrowseFFUFile();
                return 0;
            case IDC_REFRESHBTN:
                RefreshDiskList();
                AppendOutputLine(L"磁盘列表已刷新");
                return 0;
            case IDC_STARTBTN:
                StartApply();
                return 0;
            }
        }
        break;
    }

    case WM_DISM_OUTPUT:
    {
        std::wstring* pLine = (std::wstring*)wParam;
        int progress = (int)lParam;

        if (pLine)
        {
            if (!pLine->empty())
                AppendOutputLine(*pLine);
            delete pLine;
        }

        if (progress >= 0 && progress <= 100)
        {
            SendMessageW(g_hProgress, PBM_SETPOS, progress, 0);
            SetStatus(L"正在释放... " + std::to_wstring(progress) + L"%");
        }
        return 0;
    }

    case WM_DISM_DONE:
    {
        ApplyResultMsg* pMsg = (ApplyResultMsg*)lParam;

        if (pMsg)
        {
            AppendOutputLine(L"");
            if (pMsg->success)
            {
                SendMessageW(g_hProgress, PBM_SETPOS, 100, 0);
                AppendOutputLine(L"=== FFU 释放成功 ===");
                SetStatus(L"完成 - FFU 释放成功");
                MessageBoxW(g_hWnd, L"FFU 镜像释放成功!", L"完成", MB_OK | MB_ICONINFORMATION);
            }
            else
            {
                AppendOutputLine(L"=== FFU 释放失败 ===");
                AppendOutputLine(L"退出码: 0x" + std::to_wstring(pMsg->exitCode));
                if (!pMsg->errorMessage.empty())
                {
                    AppendOutputLine(L"错误信息:");
                    AppendOutputLine(pMsg->errorMessage);
                }
                SetStatus(L"失败 - 退出码 0x" + std::to_wstring(pMsg->exitCode));

                std::wstring errBox = L"FFU 释放失败!\r\n\r\n退出码: 0x" + std::to_wstring(pMsg->exitCode);
                if (!pMsg->errorMessage.empty())
                    errBox += L"\r\n\r\n" + pMsg->errorMessage;
                MessageBoxW(g_hWnd, errBox.c_str(), L"失败", MB_OK | MB_ICONERROR);
            }
            delete pMsg;
        }

        if (g_hWorkerThread)
        {
            CloseHandle(g_hWorkerThread);
            g_hWorkerThread = NULL;
        }

        g_isRunning = false;
        SetControlsEnabled(true);

        return 0;
    }

    case WM_DESTROY:
        if (g_hWorkerThread)
        {
            
            WaitForSingleObject(g_hWorkerThread, 3000);
            CloseHandle(g_hWorkerThread);
            g_hWorkerThread = NULL;
        }
        PostQuitMessage(0);
        return 0;
    }

    return DefWindowProcW(hWnd, msg, wParam, lParam);
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nCmdShow)
{
    g_hInst = hInstance;

    INITCOMMONCONTROLSEX icex = {};
    icex.dwSize = sizeof(icex);
    icex.dwICC = ICC_PROGRESS_CLASS;
    InitCommonControlsEx(&icex);

    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = L"FFUExtWindow";
    wc.hIcon = LoadIcon(NULL, IDI_APPLICATION);
    wc.hIconSm = LoadIcon(NULL, IDI_APPLICATION);

    if (!RegisterClassExW(&wc))
    {
        MessageBoxW(NULL, L"窗口类注册失败", L"错误", MB_OK | MB_ICONERROR);
        return 1;
    }

    RECT rc = { 0, 0, 660, 500 };
    AdjustWindowRectEx(&rc, WS_OVERLAPPEDWINDOW & ~WS_THICKFRAME & ~WS_MAXIMIZEBOX, FALSE, 0);

    g_hWnd = CreateWindowExW(
        0,
        L"FFUExtWindow",
        L"ffuext - FFU 镜像释放工具",
        WS_OVERLAPPEDWINDOW & ~WS_THICKFRAME & ~WS_MAXIMIZEBOX,
        CW_USEDEFAULT, CW_USEDEFAULT,
        rc.right - rc.left, rc.bottom - rc.top,
        NULL, NULL, hInstance, NULL);

    if (!g_hWnd)
    {
        MessageBoxW(NULL, L"窗口创建失败", L"错误", MB_OK | MB_ICONERROR);
        return 1;
    }

    ShowWindow(g_hWnd, nCmdShow);
    UpdateWindow(g_hWnd);

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    return (int)msg.wParam;
}
