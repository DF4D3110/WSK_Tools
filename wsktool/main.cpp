



#include <windows.h>
#include <commctrl.h>
#include <commdlg.h>
#include <shlobj.h>
#include <string>
#include <vector>
#include <sstream>

#include "wsk.h"
#include "../common/lang.h"
#include "strings.h"

static const wchar_t* PROG_NAME = L"wsktool";

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "shell32.lib")




#define IDC_WSKEDIT       1001
#define IDC_WSKBROWSE     1002
#define IDC_WSKDETECT     1003
#define IDC_WORKSPACEEDIT 1004
#define IDC_WORKSPACEBTN  1005
#define IDC_ARCHCOMBO     1006
#define IDC_PRODUCTCOMBO  1007
#define IDC_RADIO_PHYS    1008
#define IDC_RADIO_VM      1009
#define IDC_BUILDBTN      1010
#define IDC_OUTPUT        1011
#define IDC_STATUS        1012


#define WM_WSK_OUTPUT    (WM_USER + 100)
#define WM_WSK_DONE      (WM_USER + 101)
#define WM_WSK_SELECT_XML (WM_USER + 102)


#define IDC_XMLLIST   2001
#define IDC_XMLOK     2002
#define IDC_XMLCANCEL 2003




static HINSTANCE g_hInst = NULL;
static HWND g_hWnd = NULL;
static HWND g_hWskEdit = NULL;
static HWND g_hWskBrowseBtn = NULL;
static HWND g_hWskDetectBtn = NULL;
static HWND g_hWorkspaceEdit = NULL;
static HWND g_hWorkspaceBtn = NULL;
static HWND g_hArchCombo = NULL;
static HWND g_hProductCombo = NULL;
static HWND g_hRadioPhys = NULL;
static HWND g_hRadioVM = NULL;
static HWND g_hBuildBtn = NULL;
static HWND g_hOutput = NULL;
static HWND g_hStatus = NULL;
static HMENU g_hMenu = NULL;
static HFONT g_hFont = NULL;
static HWND g_lblWsk = NULL;
static HWND g_lblWorkspace = NULL;
static HWND g_lblArch = NULL;
static HWND g_lblProduct = NULL;
static HWND g_lblOutput = NULL;

static bool g_isRunning = false;
static HANDLE g_hWorkerThread = NULL;


static std::vector<std::wstring> g_xmlFiles;
static std::wstring g_selectedXml;
static HANDLE g_hXmlEvent = NULL;


static std::wstring g_outputBuffer;
static CRITICAL_SECTION g_outputLock;
#define IDT_OUTPUT_FLUSH 1


struct WskDoneMsg
{
    WskBuildResult result;
};




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


static void FlushOutputBuffer()
{
    if (g_outputBuffer.empty()) return;
    EnterCriticalSection(&g_outputLock);
    std::wstring chunk = std::move(g_outputBuffer);
    g_outputBuffer.clear();
    LeaveCriticalSection(&g_outputLock);
    if (!chunk.empty())
        AppendOutput(chunk);
}

static void SetStatus(const std::wstring& text)
{
    if (g_hStatus)
        SetWindowTextW(g_hStatus, text.c_str());
}

static void SetControlsEnabled(bool enabled)
{
    EnableWindow(g_hWskEdit, enabled);
    EnableWindow(g_hWskBrowseBtn, enabled);
    EnableWindow(g_hWskDetectBtn, enabled);
    EnableWindow(g_hWorkspaceEdit, enabled);
    EnableWindow(g_hWorkspaceBtn, enabled);
    EnableWindow(g_hArchCombo, enabled);
    EnableWindow(g_hProductCombo, enabled);
    EnableWindow(g_hRadioPhys, enabled);
    EnableWindow(g_hRadioVM, enabled);
    EnableWindow(g_hBuildBtn, enabled);
}


static std::wstring GetEditText(HWND hEdit)
{
    int len = GetWindowTextLengthW(hEdit);
    if (len == 0) return L"";
    std::vector<wchar_t> buf(len + 1);
    GetWindowTextW(hEdit, buf.data(), len + 1);
    return std::wstring(buf.data());
}




static std::wstring BrowseForFolder(HWND hParent, const std::wstring& title)
{
    std::wstring result;

    BROWSEINFOW bi = {};
    bi.hwndOwner = hParent;
    bi.lpszTitle = title.c_str();
    bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;

    LPITEMIDLIST pidl = SHBrowseForFolderW(&bi);
    if (pidl)
    {
        wchar_t path[MAX_PATH];
        if (SHGetPathFromIDListW(pidl, path))
        {
            result = path;
        }
        CoTaskMemFree(pidl);
    }

    return result;
}




static void RefreshProductList()
{
    SendMessageW(g_hProductCombo, CB_RESETCONTENT, 0, 0);

    std::wstring wskRoot = GetEditText(g_hWskEdit);
    if (wskRoot.empty() || !IsValidWskRoot(wskRoot))
    {
        SendMessageW(g_hProductCombo, CB_ADDSTRING, 0, (LPARAM)L"(请先选择有效的 WSK 目录)");
        EnableWindow(g_hBuildBtn, FALSE);
        return;
    }

    int archSel = (int)SendMessageW(g_hArchCombo, CB_GETCURSEL, 0, 0);
    if (archSel == CB_ERR)
    {
        SendMessageW(g_hProductCombo, CB_ADDSTRING, 0, (LPARAM)L"(请选择架构)");
        EnableWindow(g_hBuildBtn, FALSE);
        return;
    }

    wchar_t archName[64] = {};
    SendMessageW(g_hArchCombo, CB_GETLBTEXT, archSel, (LPARAM)archName);

    std::wstring prepArch = GuiArchToPrepArch(archName);
    std::vector<std::wstring> products = EnumerateProducts(wskRoot, prepArch);

    if (products.empty())
    {
        SendMessageW(g_hProductCombo, CB_ADDSTRING, 0, (LPARAM)L"(该架构下未找到产品)");
        EnableWindow(g_hBuildBtn, FALSE);
        AppendOutputLine(L"[警告] 在 " + prepArch + L" 架构下未找到任何产品/SKU");
    }
    else
    {
        for (const auto& p : products)
        {
            SendMessageW(g_hProductCombo, CB_ADDSTRING, 0, (LPARAM)p.c_str());
        }
        SendMessageW(g_hProductCombo, CB_SETCURSEL, 0, 0);
        EnableWindow(g_hBuildBtn, TRUE);
        AppendOutputLine(L"[信息] 检测到 " + std::to_wstring(products.size()) + L" 个产品/SKU");
    }
}




static void OnWskOutput(const std::wstring& line, void* userData)
{
    EnterCriticalSection(&g_outputLock);
    g_outputBuffer += line;
    g_outputBuffer += L"\r\n";
    LeaveCriticalSection(&g_outputLock);
}




static DWORD WINAPI WorkerThread(LPVOID param)
{
    WskBuildParams* p = (WskBuildParams*)param;

    
    WskBuildResult prepResult = RunWskPrep(*p, OnWskOutput, NULL);

    if (!prepResult.success)
    {
        WskDoneMsg* pMsg = new WskDoneMsg();
        pMsg->result = prepResult;
        PostMessageW(g_hWnd, WM_WSK_DONE, 0, (LPARAM)pMsg);
        delete p;
        return 0;
    }

    
    g_xmlFiles = EnumerateWorkspaceXml(p->workspace);
    if (g_xmlFiles.empty())
    {
        WskBuildResult r = {};
        r.success = false;
        r.errorMessage = L"工作区中未找到 XML 文件";
        WskDoneMsg* pMsg = new WskDoneMsg();
        pMsg->result = r;
        PostMessageW(g_hWnd, WM_WSK_DONE, 0, (LPARAM)pMsg);
        delete p;
        return 0;
    }

    
    g_selectedXml.clear();
    g_hXmlEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    PostMessageW(g_hWnd, WM_WSK_SELECT_XML, 0, 0);

    
    WaitForSingleObject(g_hXmlEvent, INFINITE);
    CloseHandle(g_hXmlEvent);
    g_hXmlEvent = NULL;

    if (g_selectedXml.empty())
    {
        
        WskBuildResult r = {};
        r.success = false;
        r.errorMessage = L"用户取消了 XML 选择";
        WskDoneMsg* pMsg = new WskDoneMsg();
        pMsg->result = r;
        PostMessageW(g_hWnd, WM_WSK_DONE, 0, (LPARAM)pMsg);
        delete p;
        return 0;
    }

    
    WskBuildResult buildResult = RunWskBuildImage(*p, g_selectedXml, OnWskOutput, NULL);

    WskDoneMsg* pMsg = new WskDoneMsg();
    pMsg->result = buildResult;
    PostMessageW(g_hWnd, WM_WSK_DONE, 0, (LPARAM)pMsg);

    delete p;
    return 0;
}




static void StartBuild()
{
    if (g_isRunning) return;

    
    std::wstring wskRoot = GetEditText(g_hWskEdit);
    if (wskRoot.empty() || !IsValidWskRoot(wskRoot))
    {
        MessageBoxW(g_hWnd, L"请选择有效的 WSK 目录 (包含 SetImagGenEnv.cmd)", L"提示", MB_OK | MB_ICONWARNING);
        return;
    }

    
    std::wstring workspace = GetEditText(g_hWorkspaceEdit);
    if (workspace.empty())
    {
        MessageBoxW(g_hWnd, L"请选择工作区目录", L"提示", MB_OK | MB_ICONWARNING);
        return;
    }

    
    int archSel = (int)SendMessageW(g_hArchCombo, CB_GETCURSEL, 0, 0);
    if (archSel == CB_ERR)
    {
        MessageBoxW(g_hWnd, L"请选择架构", L"提示", MB_OK | MB_ICONWARNING);
        return;
    }
    wchar_t archName[64] = {};
    SendMessageW(g_hArchCombo, CB_GETLBTEXT, archSel, (LPARAM)archName);
    std::wstring prepArch = GuiArchToPrepArch(archName);

    
    int prodSel = (int)SendMessageW(g_hProductCombo, CB_GETCURSEL, 0, 0);
    if (prodSel == CB_ERR)
    {
        MessageBoxW(g_hWnd, L"请选择产品/SKU", L"提示", MB_OK | MB_ICONWARNING);
        return;
    }
    wchar_t productName[256] = {};
    SendMessageW(g_hProductCombo, CB_GETLBTEXT, prodSel, (LPARAM)productName);

    
    bool isVM = (SendMessageW(g_hRadioVM, BM_GETCHECK, 0, 0) == BST_CHECKED);

    
    std::wstringstream confirm;
    confirm << L"即将开始 WSK 构建:\r\n\r\n";
    confirm << L"WSK: " << wskRoot << L"\r\n";
    confirm << L"工作区: " << workspace << L"\r\n";
    confirm << L"产品: " << productName << L"\r\n";
    confirm << L"架构: " << prepArch << L"\r\n";
    confirm << L"类型: " << (isVM ? L"虚拟机 (VM)" : L"实体机") << L"\r\n\r\n";
    confirm << L"构建过程可能需要较长时间, 是否继续?";

    if (MessageBoxW(g_hWnd, confirm.str().c_str(), L"确认构建", MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES)
        return;

    
    SetWindowTextW(g_hOutput, L"");

    g_isRunning = true;
    SetControlsEnabled(false);
    SetStatus(L"正在构建 WSK 映像...");

    AppendOutputLine(L"=== wsktool - WSK 构建开始 ===");
    AppendOutputLine(L"");

    
    WskBuildParams* p = new WskBuildParams();
    p->wskRoot = wskRoot;
    p->workspace = workspace;
    p->product = productName;
    p->architecture = prepArch;
    p->isVM = isVM;

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
    SetWindowTextW(g_hWnd, Lang::GetStr(IDS_APP_TITLE, L"wsktool").c_str());
    if (g_lblWsk) SetWindowTextW(g_lblWsk, Lang::GetStr(IDS_LBL_WSK, L"WSK 路径:").c_str());
    if (g_hWskBrowseBtn) SetWindowTextW(g_hWskBrowseBtn, Lang::GetStr(IDS_BTN_BROWSE, L"浏览...").c_str());
    if (g_hWskDetectBtn) SetWindowTextW(g_hWskDetectBtn, Lang::GetStr(IDS_BTN_DETECT, L"自动检测").c_str());
    if (g_lblWorkspace) SetWindowTextW(g_lblWorkspace, Lang::GetStr(IDS_LBL_WORKSPACE, L"工作区:").c_str());
    if (g_hWorkspaceBtn) SetWindowTextW(g_hWorkspaceBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());
    if (g_lblArch) SetWindowTextW(g_lblArch, Lang::GetStr(IDS_LBL_ARCH, L"架构:").c_str());
    if (g_lblProduct) SetWindowTextW(g_lblProduct, Lang::GetStr(IDS_LBL_PRODUCT, L"SKU:").c_str());
    if (g_hRadioPhys) SetWindowTextW(g_hRadioPhys, Lang::GetStr(IDS_RADIO_PHYS, L"实体机").c_str());
    if (g_hRadioVM) SetWindowTextW(g_hRadioVM, Lang::GetStr(IDS_RADIO_VM, L"虚拟机").c_str());
    if (g_hBuildBtn) SetWindowTextW(g_hBuildBtn, Lang::GetStr(IDS_BTN_BUILD, L"开始构建").c_str());
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
        CreateWindowExW(0, L"STATIC", L"wsktool v1.0.0.0", WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 15, 280, 24, hWnd, NULL, NULL, NULL);
        CreateWindowExW(0, L"STATIC", L"WinStory 2026", WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 45, 280, 20, hWnd, NULL, NULL, NULL);
        HWND hLink = CreateWindowExW(0, L"STATIC", L"https://wiki.win-story.cn", WS_CHILD | WS_VISIBLE | SS_CENTER | SS_NOTIFY, 10, 68, 280, 20, hWnd, (HMENU)4001, NULL, NULL);
        CreateWindowExW(0, L"STATIC", L"Compiled by DF4D3110", WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 95, 280, 20, hWnd, NULL, NULL, NULL);
        CreateWindowExW(0, L"BUTTON", L"OK", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON | BS_DEFPUSHBUTTON, 110, 130, 80, 28, hWnd, (HMENU)IDOK, NULL, NULL);
        EnumChildWindows(hWnd, [](HWND h, LPARAM lp) -> BOOL { SendMessageW(h, WM_SETFONT, (WPARAM)lp, TRUE); return TRUE; }, (LPARAM)hFont);
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
    if (!reg) { WNDCLASSEXW wc = {}; wc.cbSize=sizeof(wc); wc.lpfnWndProc=AboutDlgProc; wc.hInstance=GetModuleHandleW(NULL); wc.hCursor=LoadCursor(NULL,IDC_ARROW); wc.hbrBackground=(HBRUSH)(COLOR_BTNFACE+1); wc.lpszClassName=L"AboutDlgWSK"; RegisterClassExW(&wc); reg=true; }
    RECT rc={0,0,300,180}; AdjustWindowRectEx(&rc,WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|DS_MODALFRAME,FALSE,0);
    int w=rc.right-rc.left,h=rc.bottom-rc.top; RECT prc; GetWindowRect(parent,&prc);
    HWND d=CreateWindowExW(0,L"AboutDlgWSK",L"关于 wsktool",WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|DS_MODALFRAME,prc.left+(prc.right-prc.left-w)/2,prc.top+(prc.bottom-prc.top-h)/2,w,h,parent,NULL,GetModuleHandleW(NULL),NULL);
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

    
    g_lblWsk = CreateWindowW(L"STATIC", L"WSK 目录:", WS_CHILD | WS_VISIBLE,
        12, 12, 65, 20, hWnd, NULL, g_hInst, NULL);

    g_hWskEdit = CreateWindowW(L"EDIT", L"",
        WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL | ES_READONLY,
        82, 10, 400, 24, hWnd, (HMENU)IDC_WSKEDIT, g_hInst, NULL);

    g_hWskBrowseBtn = CreateWindowW(L"BUTTON", L"浏览...",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
        488, 9, 70, 26, hWnd, (HMENU)IDC_WSKBROWSE, g_hInst, NULL);

    g_hWskDetectBtn = CreateWindowW(L"BUTTON", L"自动检测",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
        564, 9, 80, 26, hWnd, (HMENU)IDC_WSKDETECT, g_hInst, NULL);

    
    g_lblWorkspace = CreateWindowW(L"STATIC", L"工作区:", WS_CHILD | WS_VISIBLE,
        12, 44, 65, 20, hWnd, NULL, g_hInst, NULL);

    g_hWorkspaceEdit = CreateWindowW(L"EDIT", L"",
        WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
        82, 42, 476, 24, hWnd, (HMENU)IDC_WORKSPACEEDIT, g_hInst, NULL);

    g_hWorkspaceBtn = CreateWindowW(L"BUTTON", L"选择...",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
        564, 41, 80, 26, hWnd, (HMENU)IDC_WORKSPACEBTN, g_hInst, NULL);

    
    g_lblArch = CreateWindowW(L"STATIC", L"架构:", WS_CHILD | WS_VISIBLE,
        12, 76, 65, 20, hWnd, NULL, g_hInst, NULL);

    g_hArchCombo = CreateWindowW(L"COMBOBOX", L"",
        WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST | WS_VSCROLL,
        82, 74, 150, 200, hWnd, (HMENU)IDC_ARCHCOMBO, g_hInst, NULL);

    
    g_lblProduct = CreateWindowW(L"STATIC", L"产品/SKU:", WS_CHILD | WS_VISIBLE,
        250, 76, 65, 20, hWnd, NULL, g_hInst, NULL);

    g_hProductCombo = CreateWindowW(L"COMBOBOX", L"",
        WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST | WS_VSCROLL,
        320, 74, 238, 200, hWnd, (HMENU)IDC_PRODUCTCOMBO, g_hInst, NULL);

    
    CreateWindowW(L"STATIC", L"类型:", WS_CHILD | WS_VISIBLE,
        12, 108, 65, 20, hWnd, NULL, g_hInst, NULL);

    g_hRadioPhys = CreateWindowW(L"BUTTON", L"实体机 (FFU)",
        WS_CHILD | WS_VISIBLE | BS_AUTORADIOBUTTON,
        82, 106, 140, 22, hWnd, (HMENU)IDC_RADIO_PHYS, g_hInst, NULL);

    g_hRadioVM = CreateWindowW(L"BUTTON", L"虚拟机 (VHDX)",
        WS_CHILD | WS_VISIBLE | BS_AUTORADIOBUTTON,
        230, 106, 140, 22, hWnd, (HMENU)IDC_RADIO_VM, g_hInst, NULL);

    SendMessageW(g_hRadioPhys, BM_SETCHECK, BST_CHECKED, 0);

    
    g_hBuildBtn = CreateWindowW(L"BUTTON", L"开始构建",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
        544, 102, 100, 30, hWnd, (HMENU)IDC_BUILDBTN, g_hInst, NULL);

    
    g_lblOutput = CreateWindowW(L"STATIC", L"构建输出:", WS_CHILD | WS_VISIBLE,
        12, 140, 80, 20, hWnd, NULL, g_hInst, NULL);

    
    g_hOutput = CreateWindowW(L"EDIT", L"",
        WS_CHILD | WS_VISIBLE | WS_BORDER | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
        12, 162, 636, 310, hWnd, (HMENU)IDC_OUTPUT, g_hInst, NULL);

    
    g_hStatus = CreateWindowW(L"STATIC", L"就绪",
        WS_CHILD | WS_VISIBLE | SS_LEFT,
        12, 480, 636, 20, hWnd, (HMENU)IDC_STATUS, g_hInst, NULL);

    
    const wchar_t* archs[] = { L"x86", L"amd64", L"arm32", L"arm64" };
    for (const wchar_t* a : archs)
        SendMessageW(g_hArchCombo, CB_ADDSTRING, 0, (LPARAM)a);
    SendMessageW(g_hArchCombo, CB_SETCURSEL, 1, 0); 

    
    EnumChildWindows(hWnd, [](HWND hChild, LPARAM lParam) -> BOOL {
        SendMessageW(hChild, WM_SETFONT, (WPARAM)lParam, TRUE);
        return TRUE;
    }, (LPARAM)hFont);
}







static HWND g_hXmlDlg = NULL;
static HWND g_hXmlList = NULL;
static int g_xmlDlgResult = 0; 

static LRESULT CALLBACK XmlDlgWndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        
        g_hXmlList = CreateWindowExW(0, L"LISTBOX", L"",
            WS_CHILD | WS_VISIBLE | WS_VSCROLL | LBS_NOTIFY | LBS_SORT,
            10, 10, 460, 280, hWnd, (HMENU)IDC_XMLLIST, g_hInst, NULL);
        
        for (size_t i = 0; i < g_xmlFiles.size(); i++)
        {
            const std::wstring& path = g_xmlFiles[i];
            size_t pos = path.find_last_of(L"\\/");
            std::wstring name = (pos != std::wstring::npos) ? path.substr(pos + 1) : path;
            SendMessageW(g_hXmlList, LB_ADDSTRING, 0, (LPARAM)name.c_str());
        }
        
        for (size_t i = 0; i < g_xmlFiles.size(); i++)
        {
            if (g_xmlFiles[i].find(L"_Configuration.xml") == std::wstring::npos)
            {
                
                const std::wstring& path = g_xmlFiles[i];
                size_t pos = path.find_last_of(L"\\/");
                std::wstring name = (pos != std::wstring::npos) ? path.substr(pos + 1) : path;
                LRESULT idx = SendMessageW(g_hXmlList, LB_FINDSTRINGEXACT, -1, (LPARAM)name.c_str());
                if (idx != LB_ERR) SendMessageW(g_hXmlList, LB_SETCURSEL, idx, 0);
                break;
            }
        }
        if (SendMessageW(g_hXmlList, LB_GETCURSEL, 0, 0) == LB_ERR)
            SendMessageW(g_hXmlList, LB_SETCURSEL, 0, 0);

        
        CreateWindowExW(0, L"BUTTON", L"确定",
            WS_CHILD | WS_VISIBLE | BS_DEFPUSHBUTTON,
            230, 300, 100, 30, hWnd, (HMENU)IDC_XMLOK, g_hInst, NULL);
        
        CreateWindowExW(0, L"BUTTON", L"取消",
            WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            340, 300, 100, 30, hWnd, (HMENU)IDC_XMLCANCEL, g_hInst, NULL);
        return 0;
    }
    case WM_COMMAND:
    {
        WORD id = LOWORD(wParam);
        WORD code = HIWORD(wParam);
        if (id == IDC_XMLOK && code == BN_CLICKED)
        {
            int sel = (int)SendMessageW(g_hXmlList, LB_GETCURSEL, 0, 0);
            if (sel != LB_ERR)
            {
                wchar_t name[512] = {};
                SendMessageW(g_hXmlList, LB_GETTEXT, sel, (LPARAM)name);
                
                for (const auto& f : g_xmlFiles)
                {
                    size_t pos = f.find_last_of(L"\\/");
                    std::wstring fname = (pos != std::wstring::npos) ? f.substr(pos + 1) : f;
                    if (fname == name) { g_selectedXml = f; break; }
                }
            }
            g_xmlDlgResult = IDOK;
            EnableWindow(g_hWnd, TRUE);
            DestroyWindow(hWnd);
            g_hXmlDlg = NULL;
            return 0;
        }
        if (id == IDC_XMLCANCEL && code == BN_CLICKED)
        {
            g_selectedXml.clear();
            g_xmlDlgResult = IDCANCEL;
            EnableWindow(g_hWnd, TRUE);
            DestroyWindow(hWnd);
            g_hXmlDlg = NULL;
            return 0;
        }
        if (id == IDC_XMLLIST && code == LBN_DBLCLK)
        {
            SendMessageW(hWnd, WM_COMMAND, MAKEWPARAM(IDC_XMLOK, BN_CLICKED), 0);
            return 0;
        }
        break;
    }
    case WM_CLOSE:
        g_selectedXml.clear();
        g_xmlDlgResult = IDCANCEL;
        EnableWindow(g_hWnd, TRUE);
        DestroyWindow(hWnd);
        g_hXmlDlg = NULL;
        return 0;
    }
    return DefWindowProcW(hWnd, msg, wParam, lParam);
}

static void ShowXmlSelectModal(HWND hParent)
{
    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = XmlDlgWndProc;
    wc.hInstance = g_hInst;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = L"XmlSelectDlgClass";
    RegisterClassExW(&wc);

    EnableWindow(hParent, FALSE);

    g_hXmlDlg = CreateWindowExW(WS_EX_DLGMODALFRAME | WS_EX_TOPMOST,
        L"XmlSelectDlgClass", L"选择要构建的 XML 文件",
        WS_POPUP | WS_CAPTION | WS_SYSMENU,
        CW_USEDEFAULT, CW_USEDEFAULT, 490, 380,
        hParent, NULL, g_hInst, NULL);

    ShowWindow(g_hXmlDlg, SW_SHOW);
    UpdateWindow(g_hXmlDlg);

    MSG msg;
    while (g_hXmlDlg && GetMessageW(&msg, NULL, 0, 0))
    {
        if (!IsDialogMessageW(g_hXmlDlg, &msg))
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
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

        AppendOutputLine(L"=== wsktool - Windows System Kit 构建工具 ===");
#if defined(_M_X64)
        AppendOutputLine(L"架构: amd64");
#elif defined(_M_IX86)
        AppendOutputLine(L"架构: x86");
#elif defined(_M_ARM64)
        AppendOutputLine(L"架构: arm64");
#elif defined(_M_ARM)
        AppendOutputLine(L"架构: arm32");
#endif
        AppendOutputLine(L"");

        
        AppendOutputLine(L"正在自动检测 WSK 位置...");
        std::wstring detected = DetectWskLocation();
        if (!detected.empty())
        {
            SetWindowTextW(g_hWskEdit, detected.c_str());
            std::wstring version = GetWskVersion(detected);
            AppendOutputLine(L"检测到 WSK: " + detected + L" (版本: " + version + L")");
            SetStatus(L"已检测到 WSK - 请选择架构和产品");
            RefreshProductList();
        }
        else
        {
            AppendOutputLine(L"未自动检测到 WSK, 请手动浏览选择目录");
            SetStatus(L"未检测到 WSK - 请手动选择目录");
            SendMessageW(g_hProductCombo, CB_ADDSTRING, 0, (LPARAM)L"(请先选择 WSK 目录)");
            EnableWindow(g_hBuildBtn, FALSE);
        }

        
        SetTimer(hWnd, IDT_OUTPUT_FLUSH, 100, NULL);

        return 0;
    }

    case WM_COMMAND:
    {
        WORD id = LOWORD(wParam);
        WORD code = HIWORD(wParam);

        
        if (id == 3001) 
        {
            std::wstring newLang = Lang::ShowDialog(hWnd, PROG_NAME);
            if (!newLang.empty() && newLang != Lang::GetCurrent())
            {
                if (Lang::Load(PROG_NAME, newLang)) ApplyLanguage();
            }
            return 0;
        }
        if (id == 3002) 
        {
            ShowAboutDialog(hWnd);
            return 0;
        }

        if (code == BN_CLICKED)
        {
            switch (id)
            {
            case IDC_WSKBROWSE:
            {
                std::wstring folder = BrowseForFolder(hWnd, L"选择 WSK 目录 (包含 SetImagGenEnv.cmd)");
                if (!folder.empty())
                {
                    if (IsValidWskRoot(folder))
                    {
                        SetWindowTextW(g_hWskEdit, folder.c_str());
                        std::wstring version = GetWskVersion(folder);
                        AppendOutputLine(L"已选择 WSK: " + folder + L" (版本: " + version + L")");
                        RefreshProductList();
                    }
                    else
                    {
                        MessageBoxW(hWnd, L"所选目录不是有效的 WSK 根目录 (未找到 SetImagGenEnv.cmd)", L"提示", MB_OK | MB_ICONWARNING);
                    }
                }
                return 0;
            }
            case IDC_WSKDETECT:
            {
                AppendOutputLine(L"正在重新检测 WSK 位置...");
                std::wstring detected = DetectWskLocation();
                if (!detected.empty())
                {
                    SetWindowTextW(g_hWskEdit, detected.c_str());
                    std::wstring version = GetWskVersion(detected);
                    AppendOutputLine(L"检测到 WSK: " + detected + L" (版本: " + version + L")");
                    RefreshProductList();
                }
                else
                {
                    AppendOutputLine(L"未检测到 WSK");
                    MessageBoxW(hWnd, L"未检测到 WSK 光盘, 请手动浏览选择目录", L"提示", MB_OK | MB_ICONINFORMATION);
                }
                return 0;
            }
            case IDC_WORKSPACEBTN:
            {
                std::wstring folder = BrowseForFolder(hWnd, L"选择工作区目录");
                if (!folder.empty())
                {
                    SetWindowTextW(g_hWorkspaceEdit, folder.c_str());
                }
                return 0;
            }
            case IDC_BUILDBTN:
                StartBuild();
                return 0;
            }
        }

        
        if (id == IDC_ARCHCOMBO && code == CBN_SELCHANGE)
        {
            
            int sel = (int)SendMessageW(g_hArchCombo, CB_GETCURSEL, 0, 0);
            if (sel != CB_ERR)
            {
                wchar_t name[64] = {};
                SendMessageW(g_hArchCombo, CB_GETLBTEXT, sel, (LPARAM)name);
                std::wstring prepArch = GuiArchToPrepArch(name);
                if (prepArch == L"Arm" || prepArch == L"Arm64")
                {
                    MessageBoxW(hWnd,
                        L"wcos在ARM/ARM64作为目标体系的情况下需要额外的设备布局，请确定你的oeminput已经添加了该内容！",
                        L"ARM/ARM64 提醒", MB_OK | MB_ICONWARNING);
                }
            }
            RefreshProductList();
            return 0;
        }

        break;
    }

    case WM_TIMER:
    {
        if (wParam == IDT_OUTPUT_FLUSH)
            FlushOutputBuffer();
        return 0;
    }

    case WM_WSK_DONE:
    {
        
        FlushOutputBuffer();

        WskDoneMsg* pMsg = (WskDoneMsg*)lParam;

        if (pMsg)
        {
            AppendOutputLine(L"");
            if (pMsg->result.success)
            {
                AppendOutputLine(L"=== WSK 构建成功 ===");
                if (!pMsg->result.outputFilePath.empty())
                {
                    AppendOutputLine(L"产物: " + pMsg->result.outputFilePath);
                    SetStatus(L"完成 - 构建成功");

                    
                    if (!pMsg->result.outputFolder.empty())
                    {
                        AppendOutputLine(L"正在打开产物文件夹: " + pMsg->result.outputFolder);
                        ShellExecuteW(NULL, L"open", pMsg->result.outputFolder.c_str(), NULL, NULL, SW_SHOWNORMAL);
                    }

                    MessageBoxW(hWnd,
                        (L"WSK 构建成功!\r\n\r\n产物: " + pMsg->result.outputFilePath).c_str(),
                        L"完成", MB_OK | MB_ICONINFORMATION);
                }
                else
                {
                    SetStatus(L"完成 - 构建成功 (未找到产物文件)");
                    MessageBoxW(hWnd, L"WSK 构建成功, 但未在工作区中找到产物文件", L"完成", MB_OK | MB_ICONWARNING);
                }
            }
            else
            {
                AppendOutputLine(L"=== WSK 构建失败 ===");
                AppendOutputLine(L"退出码: 0x" + std::to_wstring(pMsg->result.exitCode));
                if (!pMsg->result.errorMessage.empty())
                {
                    AppendOutputLine(L"错误信息:");
                    AppendOutputLine(pMsg->result.errorMessage);
                }
                SetStatus(L"失败 - 退出码 0x" + std::to_wstring(pMsg->result.exitCode));

                std::wstring errBox = L"WSK 构建失败!\r\n\r\n退出码: 0x" + std::to_wstring(pMsg->result.exitCode);
                if (!pMsg->result.errorMessage.empty())
                    errBox += L"\r\n\r\n" + pMsg->result.errorMessage;
                MessageBoxW(hWnd, errBox.c_str(), L"失败", MB_OK | MB_ICONERROR);
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

    case WM_WSK_SELECT_XML:
    {
        AppendOutputLine(L"");
        AppendOutputLine(L"=== 请选择要构建的 XML 文件 ===");
        SetStatus(L"等待用户选择 XML...");
        ShowXmlSelectModal(hWnd);
        if (g_hXmlEvent)
            SetEvent(g_hXmlEvent);
        if (!g_selectedXml.empty())
        {
            AppendOutputLine(L"已选择 XML: " + g_selectedXml);
            SetStatus(L"正在构建 WSK 映像...");
        }
        else
        {
            AppendOutputLine(L"用户取消了 XML 选择");
        }
        return 0;
    }

    case WM_DESTROY:
        KillTimer(hWnd, IDT_OUTPUT_FLUSH);
        FlushOutputBuffer();
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
    InitializeCriticalSection(&g_outputLock);

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
    wc.lpszClassName = L"WSKToolWindow";
    wc.hIcon = LoadIcon(NULL, IDI_APPLICATION);
    wc.hIconSm = LoadIcon(NULL, IDI_APPLICATION);

    if (!RegisterClassExW(&wc))
    {
        MessageBoxW(NULL, L"窗口类注册失败", L"错误", MB_OK | MB_ICONERROR);
        return 1;
    }

    RECT rc = { 0, 0, 660, 530 };
    AdjustWindowRectEx(&rc, WS_OVERLAPPEDWINDOW & ~WS_THICKFRAME & ~WS_MAXIMIZEBOX, FALSE, 0);

    g_hWnd = CreateWindowExW(
        0, L"WSKToolWindow", L"wsktool - Windows System Kit 构建工具",
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

    DeleteCriticalSection(&g_outputLock);
    return (int)msg.wParam;
}
