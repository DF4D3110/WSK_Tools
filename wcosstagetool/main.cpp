

#include <windows.h>
#include <commctrl.h>
#include <commdlg.h>
#include <shlobj.h>
#include <fstream>
#include <string>
#include <vector>
#include <sstream>
#include "../common/lang.h"
#include "strings.h"

static const wchar_t* PROG_NAME = L"wcosstagetool";

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "shell32.lib")

#define IDC_TAB          1001
#define IDC_OUTPUT       1002
#define IDC_STATUS       1003
#define IDC_RUNBTN       1012

#define IDC_IMG_TOOLEDIT 1100
#define IDC_IMG_TOOLBTN  1101
#define IDC_IMG_OUTDIR   1102
#define IDC_IMG_OUTBTN   1103
#define IDC_IMG_XMLEDIT  1104
#define IDC_IMG_XMLBTN   1105
#define IDC_IMG_PKGEDIT  1106
#define IDC_IMG_PKGBTN   1107
#define IDC_IMG_CPUCOMBO 1108
#define IDC_IMG_FFUNAME  1109

#define IDC_PAT_TOOLEDIT 1200
#define IDC_PAT_TOOLBTN  1201
#define IDC_PAT_FFUEDIT  1202
#define IDC_PAT_FFUBTN   1203
#define IDC_PAT_CPUCOMBO 1204
#define IDC_PAT_DRVEDIT  1205
#define IDC_PAT_DRVBTN   1206

#define IDC_UPD_TOOLEDIT 1300
#define IDC_UPD_TOOLBTN  1301
#define IDC_UPD_VHDEDIT  1302
#define IDC_UPD_VHDBTN   1303
#define IDC_UPD_CABEDIT  1304
#define IDC_UPD_CABBTN   1305

#define IDC_BCD_BCDEDIT  1400
#define IDC_BCD_BCDBTN   1401
#define IDC_BCD_DEBUG    1402
#define IDC_BCD_SERIAL   1403
#define IDC_BCD_PORT     1404
#define IDC_BCD_BAUD     1405
#define IDC_BCD_TESTSIGN 1406
#define IDC_BCD_NOINT    1407

#define IDT_FLUSH 1

static HINSTANCE g_hInst = NULL;
static HWND g_hWnd = NULL;
static HWND g_hTab = NULL;
static HWND g_hOutput = NULL;
static HWND g_hStatus = NULL;
static HWND g_hRunBtn = NULL;
static HFONT g_hFont = NULL;
static HMENU g_hMenu = NULL;

static HWND g_hImgToolEdit = NULL;
static HWND g_hImgToolBtn = NULL;
static HWND g_hImgOutDir = NULL;
static HWND g_hImgOutBtn = NULL;
static HWND g_hImgXmlEdit = NULL;
static HWND g_hImgXmlBtn = NULL;
static HWND g_hImgPkgEdit = NULL;
static HWND g_hImgPkgBtn = NULL;
static HWND g_hImgCpuCombo = NULL;
static HWND g_hImgFfuName = NULL;

static HWND g_hPatToolEdit = NULL;
static HWND g_hPatToolBtn = NULL;
static HWND g_hPatFfuEdit = NULL;
static HWND g_hPatFfuBtn = NULL;
static HWND g_hPatCpuCombo = NULL;
static HWND g_hPatDrvEdit = NULL;
static HWND g_hPatDrvBtn = NULL;

static HWND g_hUpdToolEdit = NULL;
static HWND g_hUpdToolBtn = NULL;
static HWND g_hUpdVhdEdit = NULL;
static HWND g_hUpdVhdBtn = NULL;
static HWND g_hUpdCabEdit = NULL;
static HWND g_hUpdCabBtn = NULL;

static HWND g_hBcdEdit = NULL;
static HWND g_hBcdBtn = NULL;
static HWND g_hBcdDebug = NULL;
static HWND g_hBcdSerial = NULL;
static HWND g_hBcdPort = NULL;
static HWND g_hBcdBaud = NULL;
static HWND g_hBcdTestsign = NULL;
static HWND g_hBcdNoint = NULL;

static HWND g_lblTab1[6] = {}; 
static HWND g_lblTab2[4] = {}; 
static HWND g_lblTab3[3] = {}; 
static HWND g_lblTab4[1] = {}; 

static int g_curTab = 0;
static bool g_isRunning = false;
static HANDLE g_hWorkerThread = NULL;

static std::wstring g_outputBuffer;
static CRITICAL_SECTION g_outputLock;

static void AppendOutput(const std::wstring& text)
{
    if (!g_hOutput) return;
    int len = GetWindowTextLengthW(g_hOutput);
    SendMessageW(g_hOutput, EM_SETSEL, (WPARAM)len, (LPARAM)len);
    SendMessageW(g_hOutput, EM_REPLACESEL, FALSE, (LPARAM)text.c_str());
    SendMessageW(g_hOutput, EM_SCROLLCARET, 0, 0);
}

static void FlushOutputBuffer()
{
    if (g_outputBuffer.empty()) return;
    EnterCriticalSection(&g_outputLock);
    std::wstring chunk = std::move(g_outputBuffer);
    g_outputBuffer.clear();
    LeaveCriticalSection(&g_outputLock);
    if (!chunk.empty()) AppendOutput(chunk);
}

static void SetStatus(const std::wstring& text)
{
    if (g_hStatus) SetWindowTextW(g_hStatus, text.c_str());
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
    BROWSEINFOW bi = {};
    bi.hwndOwner = hParent;
    bi.lpszTitle = title.c_str();
    bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
    LPITEMIDLIST pidl = SHBrowseForFolderW(&bi);
    std::wstring result;
    if (pidl)
    {
        wchar_t path[MAX_PATH];
        if (SHGetPathFromIDListW(pidl, path)) result = path;
        CoTaskMemFree(pidl);
    }
    return result;
}

static std::wstring BrowseForFile(HWND hParent, const std::wstring& title, const std::wstring& filter)
{
    wchar_t path[MAX_PATH] = {};
    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = hParent;
    ofn.lpstrFilter = filter.c_str();
    ofn.lpstrFile = path;
    ofn.nMaxFile = MAX_PATH;
    ofn.lpstrTitle = title.c_str();
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST;
    if (GetOpenFileNameW(&ofn)) return path;
    return L"";
}

static std::wstring BrowseForExe(HWND hParent, const std::wstring& exeName)
{
    std::wstring filter = exeName + L"\0" + exeName + L"\0\0";
    wchar_t path[MAX_PATH] = {};
    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = hParent;
    ofn.lpstrFilter = filter.c_str();
    ofn.lpstrFile = path;
    ofn.nMaxFile = MAX_PATH;
    ofn.lpstrTitle = (L"选择 " + exeName).c_str();
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST;
    if (GetOpenFileNameW(&ofn)) return path;
    return L"";
}

static bool FileExists(const std::wstring& path)
{
    DWORD attr = GetFileAttributesW(path.c_str());
    return attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY);
}

static std::wstring FindToolInDir(const std::wstring& dir, const std::wstring& toolName)
{
    std::wstring full = dir + L"\\" + toolName;
    if (FileExists(full)) return full;
    return L"";
}

static std::wstring AutoDetectTool(const wchar_t* toolName)
{
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(NULL, exePath, MAX_PATH);
    std::wstring exeDir = exePath;
    size_t pos = exeDir.find_last_of(L"\\/");
    if (pos != std::wstring::npos) exeDir = exeDir.substr(0, pos);

    std::vector<std::wstring> baseDirs;
    baseDirs.push_back(L"E:\\WSK_Tools\\Files\\Windows Kits\\10\\Tools\\bin\\i386");
    baseDirs.push_back(exeDir + L"\\Windows Kits\\10\\Tools\\bin\\i386");
    for (wchar_t drive = L'A'; drive <= L'Z'; drive++)
        baseDirs.push_back(std::wstring(1, drive) + L":\\Windows Kits\\10\\Tools\\bin\\i386");

    for (const auto& dir : baseDirs)
    {
        std::wstring candidate = dir + L"\\" + toolName;
        if (FileExists(candidate)) return candidate;
    }
    return L"";
}

struct ExecParams
{
    std::wstring workDir;
    std::wstring command;
};

static void OnExecOutput(const std::wstring& line, void* userData)
{
    EnterCriticalSection(&g_outputLock);
    g_outputBuffer += line;
    g_outputBuffer += L"\r\n";
    LeaveCriticalSection(&g_outputLock);
}

static DWORD WINAPI ExecThread(LPVOID param)
{
    ExecParams* p = (ExecParams*)param;
    std::wstring workDir = p->workDir;
    std::wstring command = p->command;
    delete p;

    wchar_t tempPath[MAX_PATH];
    GetTempPathW(MAX_PATH, tempPath);
    wchar_t tempFile[MAX_PATH];
    GetTempFileNameW(tempPath, L"wcos", 0, tempFile);
    std::wstring batchPath = std::wstring(tempFile) + L".bat";
    DeleteFileW(tempFile);

    {
        std::wstring content = L"@echo on\r\nchcp 65001 >nul\r\n";
        if (!workDir.empty()) content += L"cd /d \"" + workDir + L"\"\r\n";
        content += command + L"\r\n";
        std::ofstream file(batchPath, std::ios::binary);
        if (file.is_open())
        {
            int ansiLen = WideCharToMultiByte(CP_ACP, 0, content.c_str(), -1, NULL, 0, NULL, NULL);
            if (ansiLen > 0)
            {
                std::vector<char> ansiBuf(ansiLen);
                WideCharToMultiByte(CP_ACP, 0, content.c_str(), -1, ansiBuf.data(), ansiLen, NULL, NULL);
                file.write(ansiBuf.data(), ansiLen - 1);
            }
            file.close();
        }
    }

    OnExecOutput(L"> 临时批处理: " + batchPath, NULL);

    HANDLE hReadPipe = NULL, hWritePipe = NULL;
    SECURITY_ATTRIBUTES sa = {};
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;
    CreatePipe(&hReadPipe, &hWritePipe, &sa, 0);
    SetHandleInformation(hReadPipe, HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOW si = {};
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;
    si.hStdOutput = hWritePipe;
    si.hStdError = hWritePipe;

    PROCESS_INFORMATION pi = {};
    std::wstring cmdLine = L"cmd.exe /c \"" + batchPath + L"\"";
    std::vector<wchar_t> cmdBuf(cmdLine.begin(), cmdLine.end());
    cmdBuf.push_back(L'\0');

    BOOL ok = CreateProcessW(NULL, cmdBuf.data(), NULL, NULL, TRUE,
        CREATE_NO_WINDOW, NULL, NULL, &si, &pi);
    CloseHandle(hWritePipe);

    if (!ok)
    {
        OnExecOutput(L"[ERROR] 无法启动进程", NULL);
        CloseHandle(hReadPipe);
        DeleteFileW(batchPath.c_str());
        PostMessageW(g_hWnd, WM_USER + 200, 0, 0);
        return 1;
    }

    std::string rawOutput, lineBuffer;
    char buffer[4096];
    DWORD bytesRead = 0;

    while (true)
    {
        DWORD waitResult = WaitForSingleObject(pi.hProcess, 100);
        DWORD bytesAvailable = 0;
        if (PeekNamedPipe(hReadPipe, NULL, 0, NULL, &bytesAvailable, NULL) && bytesAvailable > 0)
        {
            if (ReadFile(hReadPipe, buffer, sizeof(buffer), &bytesRead, NULL) && bytesRead > 0)
            {
                rawOutput.append(buffer, bytesRead);
                lineBuffer.append(buffer, bytesRead);
                size_t nl;
                while ((nl = lineBuffer.find('\n')) != std::string::npos)
                {
                    std::string rawLine = lineBuffer.substr(0, nl);
                    lineBuffer.erase(0, nl + 1);
                    if (!rawLine.empty() && rawLine.back() == '\r') rawLine.pop_back();
                    if (!rawLine.empty())
                    {
                        int wl = MultiByteToWideChar(CP_OEMCP, 0, rawLine.c_str(), (int)rawLine.size(), NULL, 0);
                        if (wl > 0)
                        {
                            std::vector<wchar_t> wb(wl);
                            MultiByteToWideChar(CP_OEMCP, 0, rawLine.c_str(), (int)rawLine.size(), wb.data(), wl);
                            OnExecOutput(std::wstring(wb.begin(), wb.end()), NULL);
                        }
                    }
                    else OnExecOutput(L"", NULL);
                }
            }
            continue;
        }
        if (waitResult == WAIT_OBJECT_0)
        {
            while (PeekNamedPipe(hReadPipe, NULL, 0, NULL, &bytesAvailable, NULL) && bytesAvailable > 0)
            {
                if (ReadFile(hReadPipe, buffer, sizeof(buffer), &bytesRead, NULL) && bytesRead > 0)
                {
                    rawOutput.append(buffer, bytesRead);
                    lineBuffer.append(buffer, bytesRead);
                }
                else break;
            }
            break;
        }
    }

    if (!lineBuffer.empty())
    {
        if (lineBuffer.back() == '\r') lineBuffer.pop_back();
        if (!lineBuffer.empty())
        {
            int wl = MultiByteToWideChar(CP_OEMCP, 0, lineBuffer.c_str(), (int)lineBuffer.size(), NULL, 0);
            if (wl > 0)
            {
                std::vector<wchar_t> wb(wl);
                MultiByteToWideChar(CP_OEMCP, 0, lineBuffer.c_str(), (int)lineBuffer.size(), wb.data(), wl);
                OnExecOutput(std::wstring(wb.begin(), wb.end()), NULL);
            }
        }
    }

    DWORD exitCode = 0;
    GetExitCodeProcess(pi.hProcess, &exitCode);
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    CloseHandle(hReadPipe);
    DeleteFileW(batchPath.c_str());

    PostMessageW(g_hWnd, WM_USER + 200, (WPARAM)exitCode, 0);
    return (int)exitCode;
}

static void StartExec(const std::wstring& workDir, const std::wstring& command)
{
    if (g_isRunning) return;
    g_isRunning = true;
    EnableWindow(g_hRunBtn, FALSE);
    SetStatus(L"正在执行...");
    SetWindowTextW(g_hOutput, L"");

    ExecParams* p = new ExecParams();
    p->workDir = workDir;
    p->command = command;
    g_hWorkerThread = CreateThread(NULL, 0, ExecThread, p, 0, NULL);
}

static std::wstring GetExeDir(const std::wstring& exePath)
{
    size_t pos = exePath.find_last_of(L"\\/");
    if (pos != std::wstring::npos) return exePath.substr(0, pos);
    return L"";
}

static bool IsDirEmpty(const std::wstring& dir)
{
    WIN32_FIND_DATAW fd;
    std::wstring search = dir + L"\\*.*";
    HANDLE hFind = FindFirstFileW(search.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE) return true;
    int count = 0;
    do
    {
        if (wcscmp(fd.cFileName, L".") != 0 && wcscmp(fd.cFileName, L"..") != 0)
            count++;
    } while (FindNextFileW(hFind, &fd) && count < 2);
    FindClose(hFind);
    return count == 0;
}

static std::wstring SaveFfuDialog(HWND hParent, const std::wstring& initDir, const std::wstring& initName)
{
    wchar_t path[MAX_PATH] = {};
    wcscpy_s(path, initName.c_str());
    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = hParent;
    ofn.lpstrFilter = L"FFU Files\0*.ffu\0All Files\0*.*\0";
    ofn.lpstrFile = path;
    ofn.nMaxFile = MAX_PATH;
    ofn.lpstrInitialDir = initDir.c_str();
    ofn.lpstrTitle = L"保存 FFU 文件";
    ofn.Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST;
    if (GetSaveFileNameW(&ofn)) return path;
    return L"";
}

static std::wstring BuildImgGenCommand(std::wstring& workDir)
{
    std::wstring tool = GetEditText(g_hImgToolEdit);
    std::wstring outDir = GetEditText(g_hImgOutDir);
    std::wstring ffuName = GetEditText(g_hImgFfuName);
    std::wstring xml = GetEditText(g_hImgXmlEdit);
    std::wstring pkg = GetEditText(g_hImgPkgEdit);
    int cpuSel = (int)SendMessageW(g_hImgCpuCombo, CB_GETCURSEL, 0, 0);
    wchar_t cpu[32] = {};
    if (cpuSel != CB_ERR) SendMessageW(g_hImgCpuCombo, CB_GETLBTEXT, cpuSel, (LPARAM)cpu);

    if (tool.empty()) { MessageBoxW(g_hWnd, L"请选择 imggen.cmd", L"提示", MB_OK | MB_ICONWARNING); return L""; }
    if (outDir.empty() || xml.empty() || pkg.empty())
    { MessageBoxW(g_hWnd, L"请填写输出目录、OEMInput XML 和 MSPackage 根目录", L"提示", MB_OK | MB_ICONWARNING); return L""; }
    if (ffuName.empty()) ffuName = L"flash.ffu";

    DWORD attr = GetFileAttributesW(outDir.c_str());
    if (attr == INVALID_FILE_ATTRIBUTES || !(attr & FILE_ATTRIBUTE_DIRECTORY))
    {
        if (IDYES != MessageBoxW(g_hWnd, L"输出目录不存在, 是否创建?", L"提示", MB_YESNO | MB_ICONQUESTION))
            return L"";
        CreateDirectoryW(outDir.c_str(), NULL);
    }

    std::wstring outFile = outDir + L"\\" + ffuName;
    workDir = GetExeDir(tool);
    std::wstring cmd = L"\"" + tool + L"\" \"" + outFile + L"\" \"" + xml + L"\" \"" + pkg + L"\"";
    if (cpu[0]) cmd += L" " + std::wstring(cpu);
    return cmd;
}

static std::wstring BuildPatchCommand(std::wstring& workDir)
{
    std::wstring tool = GetEditText(g_hPatToolEdit);
    std::wstring ffu = GetEditText(g_hPatFfuEdit);
    std::wstring drv = GetEditText(g_hPatDrvEdit);
    int cpuSel = (int)SendMessageW(g_hPatCpuCombo, CB_GETCURSEL, 0, 0);
    wchar_t cpu[32] = {};
    if (cpuSel != CB_ERR) SendMessageW(g_hPatCpuCombo, CB_GETLBTEXT, cpuSel, (LPARAM)cpu);

    if (tool.empty()) { MessageBoxW(g_hWnd, L"请选择 imageapp.exe", L"提示", MB_OK | MB_ICONWARNING); return L""; }
    if (ffu.empty() || drv.empty() || cpu[0] == 0)
    { MessageBoxW(g_hWnd, L"请填写 FFU 路径、CPU 类型和驱动目录", L"提示", MB_OK | MB_ICONWARNING); return L""; }

    workDir = GetExeDir(tool);
    return L"\"" + tool + L"\" \"" + ffu + L"\" /CPUType:" + std::wstring(cpu) + L" /Patch /Drivers:\"" + drv + L"\"";
}

static std::wstring BuildUpdateCommand(std::wstring& workDir)
{
    std::wstring tool = GetEditText(g_hUpdToolEdit);
    std::wstring vhd = GetEditText(g_hUpdVhdEdit);
    std::wstring cab = GetEditText(g_hUpdCabEdit);

    if (tool.empty()) { MessageBoxW(g_hWnd, L"请选择 UpdateApp.exe", L"提示", MB_OK | MB_ICONWARNING); return L""; }
    if (vhd.empty() || cab.empty())
    { MessageBoxW(g_hWnd, L"请填写 VHD 路径和 CAB 文件夹路径", L"提示", MB_OK | MB_ICONWARNING); return L""; }

    workDir = GetExeDir(tool);
    return L"\"" + tool + L"\" mountandinstall \"" + vhd + L"\" \"" + cab + L"\"";
}

static std::wstring BuildBcdCommand(std::wstring& workDir)
{
    std::wstring bcd = GetEditText(g_hBcdEdit);
    if (bcd.empty()) { MessageBoxW(g_hWnd, L"请选择 BCD 文件", L"提示", MB_OK | MB_ICONWARNING); return L""; }

    workDir = L"C:\\Windows\\System32";
    std::wstring cmd;
    if (SendMessageW(g_hBcdDebug, BM_GETCHECK, 0, 0) == BST_CHECKED)
        cmd += L"bcdedit /store \"" + bcd + L"\" /set {default} debug on\r\n";
    if (SendMessageW(g_hBcdSerial, BM_GETCHECK, 0, 0) == BST_CHECKED)
        cmd += L"bcdedit /store \"" + bcd + L"\" /set {default} debugtype serial\r\n";
    if (SendMessageW(g_hBcdPort, BM_GETCHECK, 0, 0) == BST_CHECKED)
        cmd += L"bcdedit /store \"" + bcd + L"\" /set {default} debugport 1\r\n";
    if (SendMessageW(g_hBcdBaud, BM_GETCHECK, 0, 0) == BST_CHECKED)
        cmd += L"bcdedit /store \"" + bcd + L"\" /set {default} baudrate 115200\r\n";
    if (SendMessageW(g_hBcdTestsign, BM_GETCHECK, 0, 0) == BST_CHECKED)
        cmd += L"bcdedit /store \"" + bcd + L"\" /set {default} testsigning on\r\n";
    if (SendMessageW(g_hBcdNoint, BM_GETCHECK, 0, 0) == BST_CHECKED)
        cmd += L"bcdedit /store \"" + bcd + L"\" /set {default} nointegritychecks on\r\n";
    if (cmd.empty()) { MessageBoxW(g_hWnd, L"请至少勾选一个选项", L"提示", MB_OK | MB_ICONWARNING); return L""; }
    return cmd;
}

static void ShowTab(int tab)
{
    g_curTab = tab;
    bool t1 = (tab == 0), t2 = (tab == 1), t3 = (tab == 2), t4 = (tab == 3);
    int sw;

    sw = t1 ? SW_SHOW : SW_HIDE;
    for (int i = 0; i < 6; i++) if (g_lblTab1[i]) ShowWindow(g_lblTab1[i], sw);

    sw = t2 ? SW_SHOW : SW_HIDE;
    for (int i = 0; i < 4; i++) if (g_lblTab2[i]) ShowWindow(g_lblTab2[i], sw);

    sw = t3 ? SW_SHOW : SW_HIDE;
    for (int i = 0; i < 3; i++) if (g_lblTab3[i]) ShowWindow(g_lblTab3[i], sw);

    sw = t4 ? SW_SHOW : SW_HIDE;
    for (int i = 0; i < 1; i++) if (g_lblTab4[i]) ShowWindow(g_lblTab4[i], sw);

    ShowWindow(g_hImgToolEdit, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgToolBtn, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgOutDir, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgOutBtn, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgXmlEdit, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgXmlBtn, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgPkgEdit, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgPkgBtn, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgCpuCombo, t1 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hImgFfuName, t1 ? SW_SHOW : SW_HIDE);

    ShowWindow(g_hPatToolEdit, t2 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hPatToolBtn, t2 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hPatFfuEdit, t2 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hPatFfuBtn, t2 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hPatCpuCombo, t2 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hPatDrvEdit, t2 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hPatDrvBtn, t2 ? SW_SHOW : SW_HIDE);

    ShowWindow(g_hUpdToolEdit, t3 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hUpdToolBtn, t3 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hUpdVhdEdit, t3 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hUpdVhdBtn, t3 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hUpdCabEdit, t3 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hUpdCabBtn, t3 ? SW_SHOW : SW_HIDE);

    ShowWindow(g_hBcdEdit, t4 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hBcdBtn, t4 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hBcdDebug, t4 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hBcdSerial, t4 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hBcdPort, t4 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hBcdBaud, t4 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hBcdTestsign, t4 ? SW_SHOW : SW_HIDE);
    ShowWindow(g_hBcdNoint, t4 ? SW_SHOW : SW_HIDE);
}

static void ApplyLanguage()
{
    if (!g_hWnd) return;
    SetWindowTextW(g_hWnd, Lang::GetStr(IDS_APP_TITLE, L"wcosstagetool").c_str());

    TCITEMW tie = {};
    tie.mask = TCIF_TEXT;
    wchar_t buf[64];
    wcscpy_s(buf, Lang::GetStr(IDS_TAB_BUILD, L"wcos构建").c_str());
    tie.pszText = buf;
    TabCtrl_SetItem(g_hTab, 0, &tie);
    wcscpy_s(buf, Lang::GetStr(IDS_TAB_PATCH, L"驱动注入").c_str());
    TabCtrl_SetItem(g_hTab, 1, &tie);
    wcscpy_s(buf, Lang::GetStr(IDS_TAB_UPDATE, L"cab注入").c_str());
    TabCtrl_SetItem(g_hTab, 2, &tie);
    wcscpy_s(buf, Lang::GetStr(IDS_TAB_BCD, L"bcd可选").c_str());
    TabCtrl_SetItem(g_hTab, 3, &tie);

    SetWindowTextW(g_hRunBtn, Lang::GetStr(IDS_BTN_START, L"开始").c_str());
    if (g_hImgToolBtn) SetWindowTextW(g_hImgToolBtn, Lang::GetStr(IDS_BTN_BROWSE, L"浏览...").c_str());
    if (g_hImgOutBtn) SetWindowTextW(g_hImgOutBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());
    if (g_hImgXmlBtn) SetWindowTextW(g_hImgXmlBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());
    if (g_hImgPkgBtn) SetWindowTextW(g_hImgPkgBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());
    if (g_hPatToolBtn) SetWindowTextW(g_hPatToolBtn, Lang::GetStr(IDS_BTN_BROWSE, L"浏览...").c_str());
    if (g_hPatFfuBtn) SetWindowTextW(g_hPatFfuBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());
    if (g_hPatDrvBtn) SetWindowTextW(g_hPatDrvBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());
    if (g_hUpdToolBtn) SetWindowTextW(g_hUpdToolBtn, Lang::GetStr(IDS_BTN_BROWSE, L"浏览...").c_str());
    if (g_hUpdVhdBtn) SetWindowTextW(g_hUpdVhdBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());
    if (g_hUpdCabBtn) SetWindowTextW(g_hUpdCabBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());
    if (g_hBcdBtn) SetWindowTextW(g_hBcdBtn, Lang::GetStr(IDS_BTN_SELECT, L"选择...").c_str());

    if (g_lblTab1[0]) SetWindowTextW(g_lblTab1[0], Lang::GetStr(IDS_LBL_IMGGEN, L"imggen.cmd:").c_str());
    if (g_lblTab1[1]) SetWindowTextW(g_lblTab1[1], Lang::GetStr(IDS_LBL_OUTDIR, L"输出目录:").c_str());
    if (g_lblTab1[2]) SetWindowTextW(g_lblTab1[2], Lang::GetStr(IDS_LBL_FFUNAME, L"FFU 文件名:").c_str());
    if (g_lblTab1[3]) SetWindowTextW(g_lblTab1[3], Lang::GetStr(IDS_LBL_XML, L"OEMInput XML:").c_str());
    if (g_lblTab1[4]) SetWindowTextW(g_lblTab1[4], Lang::GetStr(IDS_LBL_PKG, L"MSPackage 根目录:").c_str());
    if (g_lblTab1[5]) SetWindowTextW(g_lblTab1[5], Lang::GetStr(IDS_LBL_CPU, L"CPU 类型:").c_str());
    if (g_lblTab2[0]) SetWindowTextW(g_lblTab2[0], Lang::GetStr(IDS_LBL_IMAGEAPP, L"imageapp.exe:").c_str());
    if (g_lblTab2[1]) SetWindowTextW(g_lblTab2[1], Lang::GetStr(IDS_LBL_FFU, L"FFU 文件:").c_str());
    if (g_lblTab2[2]) SetWindowTextW(g_lblTab2[2], Lang::GetStr(IDS_LBL_CPU, L"CPU 类型:").c_str());
    if (g_lblTab2[3]) SetWindowTextW(g_lblTab2[3], Lang::GetStr(IDS_LBL_DRIVER, L"驱动目录:").c_str());
    if (g_lblTab3[0]) SetWindowTextW(g_lblTab3[0], Lang::GetStr(IDS_LBL_UPDATEAPP, L"UpdateApp.exe:").c_str());
    if (g_lblTab3[1]) SetWindowTextW(g_lblTab3[1], Lang::GetStr(IDS_LBL_VHD, L"VHD 文件:").c_str());
    if (g_lblTab3[2]) SetWindowTextW(g_lblTab3[2], Lang::GetStr(IDS_LBL_CAB, L"CAB 文件夹:").c_str());
    if (g_lblTab4[0]) SetWindowTextW(g_lblTab4[0], Lang::GetStr(IDS_LBL_BCD, L"BCD 文件:").c_str());

    if (g_hBcdDebug) SetWindowTextW(g_hBcdDebug, Lang::GetStr(IDS_CHK_DEBUG, L"debug on").c_str());
    if (g_hBcdSerial) SetWindowTextW(g_hBcdSerial, Lang::GetStr(IDS_CHK_SERIAL, L"debugtype serial").c_str());
    if (g_hBcdPort) SetWindowTextW(g_hBcdPort, Lang::GetStr(IDS_CHK_PORT, L"debugport 1").c_str());
    if (g_hBcdBaud) SetWindowTextW(g_hBcdBaud, Lang::GetStr(IDS_CHK_BAUD, L"baudrate 115200").c_str());
    if (g_hBcdTestsign) SetWindowTextW(g_hBcdTestsign, Lang::GetStr(IDS_CHK_TESTSIGN, L"testsigning on").c_str());
    if (g_hBcdNoint) SetWindowTextW(g_hBcdNoint, Lang::GetStr(IDS_CHK_NOINT, L"nointegritychecks on").c_str());

    if (g_hMenu)
    {
        HMENU hLangMenu = GetSubMenu(g_hMenu, 0);
        if (hLangMenu)
        {
            ModifyMenuW(hLangMenu, 0, MF_BYPOSITION | MF_STRING, 3001, Lang::GetStr(IDS_MENU_SWITCH, L"切换语言...").c_str());
        }
        ModifyMenuW(g_hMenu, 0, MF_BYPOSITION | MF_STRING | MF_POPUP, (UINT_PTR)hLangMenu, Lang::GetStr(IDS_MENU_LANG, L"语言").c_str());

        HMENU hHelpMenu = GetSubMenu(g_hMenu, 1);
        if (hHelpMenu)
        {
            ModifyMenuW(hHelpMenu, 0, MF_BYPOSITION | MF_STRING, 3002, Lang::GetStr(IDS_MENU_ABOUT, L"关于...").c_str());
        }
        ModifyMenuW(g_hMenu, 1, MF_BYPOSITION | MF_STRING | MF_POPUP, (UINT_PTR)hHelpMenu, Lang::GetStr(IDS_MENU_HELP, L"帮助").c_str());
    }

    if (!g_isRunning && g_hStatus)
        SetWindowTextW(g_hStatus, Lang::GetStr(IDS_STATUS_READY, L"就绪").c_str());
}

static HWND CreateLabel(HWND parent, int x, int y, int w, int h, const wchar_t* text)
{
    return CreateWindowExW(0, L"STATIC", text, WS_CHILD,
        x, y, w, h, parent, NULL, g_hInst, NULL);
}

static HWND CreateEdit(HWND parent, int id, int x, int y, int w, int h)
{
    return CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"",
        WS_CHILD | WS_TABSTOP | ES_AUTOHSCROLL,
        x, y, w, h, parent, (HMENU)(INT_PTR)id, g_hInst, NULL);
}

static HWND CreateBtn(HWND parent, int id, int x, int y, int w, int h, const wchar_t* text)
{
    return CreateWindowExW(0, L"BUTTON", text,
        WS_CHILD | WS_TABSTOP | BS_PUSHBUTTON,
        x, y, w, h, parent, (HMENU)(INT_PTR)id, g_hInst, NULL);
}

static HWND CreateCombo(HWND parent, int id, int x, int y, int w, int h)
{
    return CreateWindowExW(0, L"COMBOBOX", L"",
        WS_CHILD | WS_TABSTOP | CBS_DROPDOWNLIST,
        x, y, w, h, parent, (HMENU)(INT_PTR)id, g_hInst, NULL);
}

static HWND CreateCheck(HWND parent, int id, int x, int y, int w, int h, const wchar_t* text)
{
    return CreateWindowExW(0, L"BUTTON", text,
        WS_CHILD | WS_TABSTOP | BS_AUTOCHECKBOX,
        x, y, w, h, parent, (HMENU)(INT_PTR)id, g_hInst, NULL);
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

    g_hRunBtn = CreateBtn(hWnd, IDC_RUNBTN, 600, 8, 80, 28, L"开始");
    ShowWindow(g_hRunBtn, SW_SHOW);

    g_hTab = CreateWindowExW(0, WC_TABCONTROLW, L"",
        WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
        10, 42, 580, 340, hWnd, (HMENU)IDC_TAB, g_hInst, NULL);

    TCITEMW tie = {};
    tie.mask = TCIF_TEXT;
    tie.pszText = (LPWSTR)L"wcos构建";
    TabCtrl_InsertItem(g_hTab, 0, &tie);
    tie.pszText = (LPWSTR)L"驱动注入";
    TabCtrl_InsertItem(g_hTab, 1, &tie);
    tie.pszText = (LPWSTR)L"cab注入";
    TabCtrl_InsertItem(g_hTab, 2, &tie);
    tie.pszText = (LPWSTR)L"bcd可选";
    TabCtrl_InsertItem(g_hTab, 3, &tie);

    RECT tabRect;
    GetClientRect(g_hTab, &tabRect);
    TabCtrl_AdjustRect(g_hTab, FALSE, &tabRect);
    MapWindowPoints(g_hTab, hWnd, (LPPOINT)&tabRect, 2);
    int tx = tabRect.left + 10, ty = tabRect.top + 8;
    int editW = 380, btnW = 70, labelW = 100;

    const wchar_t* cpus[] = { L"x86", L"AMD64", L"ARM", L"ARM64" };

    g_lblTab1[0] = CreateLabel(hWnd, tx, ty, labelW, 20, L"imggen.cmd:");
    g_hImgToolEdit = CreateEdit(hWnd, IDC_IMG_TOOLEDIT, tx + labelW, ty - 2, editW, 24);
    g_hImgToolBtn = CreateBtn(hWnd, IDC_IMG_TOOLBTN, tx + labelW + editW + 5, ty - 3, btnW, 26, L"浏览...");

    g_lblTab1[1] = CreateLabel(hWnd, tx, ty + 32, labelW, 20, L"输出目录:");
    g_hImgOutDir = CreateEdit(hWnd, IDC_IMG_OUTDIR, tx + labelW, ty + 30, editW, 24);
    g_hImgOutBtn = CreateBtn(hWnd, IDC_IMG_OUTBTN, tx + labelW + editW + 5, ty + 29, btnW, 26, L"选择...");

    g_lblTab1[2] = CreateLabel(hWnd, tx, ty + 64, labelW, 20, L"FFU 文件名:");
    g_hImgFfuName = CreateEdit(hWnd, IDC_IMG_FFUNAME, tx + labelW, ty + 62, editW, 24);
    SetWindowTextW(g_hImgFfuName, L"flash.ffu");

    g_lblTab1[3] = CreateLabel(hWnd, tx, ty + 96, labelW, 20, L"OEMInput XML:");
    g_hImgXmlEdit = CreateEdit(hWnd, IDC_IMG_XMLEDIT, tx + labelW, ty + 94, editW, 24);
    g_hImgXmlBtn = CreateBtn(hWnd, IDC_IMG_XMLBTN, tx + labelW + editW + 5, ty + 93, btnW, 26, L"选择...");

    g_lblTab1[4] = CreateLabel(hWnd, tx, ty + 128, labelW + 10, 20, L"MSPackage 根目录:");
    g_hImgPkgEdit = CreateEdit(hWnd, IDC_IMG_PKGEDIT, tx + labelW + 10, ty + 126, editW - 10, 24);
    g_hImgPkgBtn = CreateBtn(hWnd, IDC_IMG_PKGBTN, tx + labelW + editW + 5, ty + 125, btnW, 26, L"选择...");

    g_lblTab1[5] = CreateLabel(hWnd, tx, ty + 160, labelW, 20, L"CPU 类型:");
    g_hImgCpuCombo = CreateCombo(hWnd, IDC_IMG_CPUCOMBO, tx + labelW, ty + 158, 150, 120);
    for (int i = 0; i < 4; i++) SendMessageW(g_hImgCpuCombo, CB_ADDSTRING, 0, (LPARAM)cpus[i]);
    SendMessageW(g_hImgCpuCombo, CB_SETCURSEL, 1, 0);

    g_lblTab2[0] = CreateLabel(hWnd, tx, ty, labelW, 20, L"imageapp.exe:");
    g_hPatToolEdit = CreateEdit(hWnd, IDC_PAT_TOOLEDIT, tx + labelW, ty - 2, editW, 24);
    g_hPatToolBtn = CreateBtn(hWnd, IDC_PAT_TOOLBTN, tx + labelW + editW + 5, ty - 3, btnW, 26, L"浏览...");

    g_lblTab2[1] = CreateLabel(hWnd, tx, ty + 32, labelW, 20, L"FFU 文件:");
    g_hPatFfuEdit = CreateEdit(hWnd, IDC_PAT_FFUEDIT, tx + labelW, ty + 30, editW, 24);
    g_hPatFfuBtn = CreateBtn(hWnd, IDC_PAT_FFUBTN, tx + labelW + editW + 5, ty + 29, btnW, 26, L"选择...");

    g_lblTab2[2] = CreateLabel(hWnd, tx, ty + 64, labelW, 20, L"CPU 类型:");
    g_hPatCpuCombo = CreateCombo(hWnd, IDC_PAT_CPUCOMBO, tx + labelW, ty + 62, 150, 120);
    for (int i = 0; i < 4; i++) SendMessageW(g_hPatCpuCombo, CB_ADDSTRING, 0, (LPARAM)cpus[i]);
    SendMessageW(g_hPatCpuCombo, CB_SETCURSEL, 1, 0);

    g_lblTab2[3] = CreateLabel(hWnd, tx, ty + 96, labelW, 20, L"驱动目录:");
    g_hPatDrvEdit = CreateEdit(hWnd, IDC_PAT_DRVEDIT, tx + labelW, ty + 94, editW, 24);
    g_hPatDrvBtn = CreateBtn(hWnd, IDC_PAT_DRVBTN, tx + labelW + editW + 5, ty + 93, btnW, 26, L"选择...");

    g_lblTab3[0] = CreateLabel(hWnd, tx, ty, labelW, 20, L"UpdateApp.exe:");
    g_hUpdToolEdit = CreateEdit(hWnd, IDC_UPD_TOOLEDIT, tx + labelW, ty - 2, editW, 24);
    g_hUpdToolBtn = CreateBtn(hWnd, IDC_UPD_TOOLBTN, tx + labelW + editW + 5, ty - 3, btnW, 26, L"浏览...");

    g_lblTab3[1] = CreateLabel(hWnd, tx, ty + 32, labelW, 20, L"VHD 文件:");
    g_hUpdVhdEdit = CreateEdit(hWnd, IDC_UPD_VHDEDIT, tx + labelW, ty + 30, editW, 24);
    g_hUpdVhdBtn = CreateBtn(hWnd, IDC_UPD_VHDBTN, tx + labelW + editW + 5, ty + 29, btnW, 26, L"选择...");

    g_lblTab3[2] = CreateLabel(hWnd, tx, ty + 64, labelW, 20, L"CAB 文件夹:");
    g_hUpdCabEdit = CreateEdit(hWnd, IDC_UPD_CABEDIT, tx + labelW, ty + 62, editW, 24);
    g_hUpdCabBtn = CreateBtn(hWnd, IDC_UPD_CABBTN, tx + labelW + editW + 5, ty + 61, btnW, 26, L"选择...");

    g_lblTab4[0] = CreateLabel(hWnd, tx, ty, labelW, 20, L"BCD 文件:");
    g_hBcdEdit = CreateEdit(hWnd, IDC_BCD_BCDEDIT, tx + labelW, ty - 2, editW, 24);
    g_hBcdBtn = CreateBtn(hWnd, IDC_BCD_BCDBTN, tx + labelW + editW + 5, ty - 3, btnW, 26, L"选择...");

    g_hBcdDebug = CreateCheck(hWnd, IDC_BCD_DEBUG, tx + 10, ty + 32, 200, 20, L"debug on");
    g_hBcdSerial = CreateCheck(hWnd, IDC_BCD_SERIAL, tx + 220, ty + 32, 200, 20, L"debugtype serial");
    g_hBcdPort = CreateCheck(hWnd, IDC_BCD_PORT, tx + 10, ty + 56, 200, 20, L"debugport 1");
    g_hBcdBaud = CreateCheck(hWnd, IDC_BCD_BAUD, tx + 220, ty + 56, 200, 20, L"baudrate 115200");
    g_hBcdTestsign = CreateCheck(hWnd, IDC_BCD_TESTSIGN, tx + 10, ty + 80, 200, 20, L"testsigning on");
    g_hBcdNoint = CreateCheck(hWnd, IDC_BCD_NOINT, tx + 220, ty + 80, 250, 20, L"nointegritychecks on");

    g_hOutput = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"",
        WS_CHILD | WS_VISIBLE | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL | ES_READONLY,
        10, 392, 670, 130, hWnd, (HMENU)IDC_OUTPUT, g_hInst, NULL);

    g_hStatus = CreateWindowExW(0, L"STATIC", L"就绪",
        WS_CHILD | WS_VISIBLE | SS_LEFT,
        10, 528, 670, 20, hWnd, (HMENU)IDC_STATUS, g_hInst, NULL);

    NONCLIENTMETRICSW ncm = {};
    ncm.cbSize = sizeof(ncm);
    SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0);
    g_hFont = CreateFontIndirectW(&ncm.lfMessageFont);
    if (!g_hFont) g_hFont = (HFONT)GetStockObject(DEFAULT_GUI_FONT);

    EnumChildWindows(hWnd, [](HWND hChild, LPARAM lParam) -> BOOL {
        SendMessageW(hChild, WM_SETFONT, (WPARAM)lParam, TRUE);
        return TRUE;
    }, (LPARAM)g_hFont);

    ShowTab(0);
}

#define IDC_ABOUT_LINK 4001

static LRESULT CALLBACK AboutDlgProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        HFONT hFont = (HFONT)GetStockObject(DEFAULT_GUI_FONT);
        NONCLIENTMETRICSW ncm = {};
        ncm.cbSize = sizeof(ncm);
        SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0);
        hFont = CreateFontIndirectW(&ncm.lfMessageFont);

        CreateWindowExW(0, L"STATIC", L"wcosstagetool v1.0.0.0",
            WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 15, 280, 24, hWnd, NULL, NULL, NULL);

        CreateWindowExW(0, L"STATIC", L"WinStory 2026",
            WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 45, 280, 20, hWnd, NULL, NULL, NULL);

        HWND hLink = CreateWindowExW(0, L"STATIC", L"https://wiki.win-story.cn",
            WS_CHILD | WS_VISIBLE | SS_CENTER | SS_NOTIFY, 10, 68, 280, 20, hWnd, (HMENU)IDC_ABOUT_LINK, NULL, NULL);
        SendMessageW(hLink, WM_SETFONT, (WPARAM)hFont, TRUE);

        CreateWindowExW(0, L"STATIC", L"Compiled by DF4D3110",
            WS_CHILD | WS_VISIBLE | SS_CENTER, 10, 95, 280, 20, hWnd, NULL, NULL, NULL);

        CreateWindowExW(0, L"BUTTON", L"OK",
            WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON | BS_DEFPUSHBUTTON,
            110, 130, 80, 28, hWnd, (HMENU)IDOK, NULL, NULL);

        EnumChildWindows(hWnd, [](HWND h, LPARAM lp) -> BOOL {
            SendMessageW(h, WM_SETFONT, (WPARAM)lp, TRUE); return TRUE;
        }, (LPARAM)hFont);

        SetWindowTextW(hLink, L"https://wiki.win-story.cn");
        return 0;
    }
    case WM_CTLCOLORSTATIC:
    {
        HWND hCtrl = (HWND)lParam;
        if (GetDlgCtrlID(hCtrl) == IDC_ABOUT_LINK)
        {
            SetTextColor((HDC)wParam, RGB(0, 0, 255));
            SetBkMode((HDC)wParam, TRANSPARENT);
            return (LRESULT)GetStockObject(NULL_BRUSH);
        }
        break;
    }
    case WM_SETCURSOR:
    {
        if (LOWORD(lParam) == HTCLIENT)
        {
            HWND hLink = GetDlgItem(hWnd, IDC_ABOUT_LINK);
            POINT pt;
            GetCursorPos(&pt);
            ScreenToClient(hWnd, &pt);
            RECT rc;
            GetWindowRect(hLink, &rc);
            ScreenToClient(hWnd, (LPPOINT)&rc);
            if (PtInRect(&rc, pt))
            {
                SetCursor(LoadCursor(NULL, IDC_HAND));
                return TRUE;
            }
        }
        break;
    }
    case WM_COMMAND:
    {
        if (LOWORD(wParam) == IDC_ABOUT_LINK)
        {
            ShellExecuteW(NULL, L"open", L"https://wiki.win-story.cn", NULL, NULL, SW_SHOWNORMAL);
            return 0;
        }
        if (LOWORD(wParam) == IDOK || LOWORD(wParam) == IDCANCEL)
        {
            DestroyWindow(hWnd);
            return 0;
        }
        break;
    }
    case WM_CLOSE:
        DestroyWindow(hWnd);
        return 0;
    }
    return DefWindowProcW(hWnd, msg, wParam, lParam);
}

static void ShowAboutDialog(HWND parent)
{
    static bool registered = false;
    if (!registered)
    {
        WNDCLASSEXW wc = {};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = AboutDlgProc;
        wc.hInstance = GetModuleHandleW(NULL);
        wc.hCursor = LoadCursor(NULL, IDC_ARROW);
        wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
        wc.lpszClassName = L"AboutDialog";
        RegisterClassExW(&wc);
        registered = true;
    }

    RECT rc = { 0, 0, 300, 180 };
    AdjustWindowRectEx(&rc, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME, FALSE, 0);
    int w = rc.right - rc.left, h = rc.bottom - rc.top;
    RECT prc;
    GetWindowRect(parent, &prc);
    int x = prc.left + (prc.right - prc.left - w) / 2;
    int y = prc.top + (prc.bottom - prc.top - h) / 2;

    HWND hDlg = CreateWindowExW(0, L"AboutDialog", L"关于 wcosstagetool",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME,
        x, y, w, h, parent, NULL, GetModuleHandleW(NULL), NULL);
    if (!hDlg) return;
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
}

static LRESULT CALLBACK WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        InitializeCriticalSection(&g_outputLock);
        CreateControls(hWnd);
        SetTimer(hWnd, IDT_FLUSH, 100, NULL);

        if (!Lang::Load(PROG_NAME, L"zh-cn"))
        {
            
            wchar_t langCode[32] = {};
            GetUserDefaultLocaleName(langCode, 32);
            Lang::Load(PROG_NAME, langCode);
        }
        ApplyLanguage();

        std::wstring imgGen = AutoDetectTool(L"imggen.cmd");
        std::wstring imageApp = AutoDetectTool(L"imageapp.exe");
        std::wstring updateApp = AutoDetectTool(L"UpdateApp.exe");
        int found = 0;
        if (!imgGen.empty()) { SetWindowTextW(g_hImgToolEdit, imgGen.c_str()); found++; }
        if (!imageApp.empty()) { SetWindowTextW(g_hPatToolEdit, imageApp.c_str()); found++; }
        if (!updateApp.empty()) { SetWindowTextW(g_hUpdToolEdit, updateApp.c_str()); found++; }
        if (found > 0)
            SetStatus(L"已自动检测到 " + std::to_wstring(found) + L" 个工具路径");
        else
            SetStatus(L"未检测到工具路径, 请手动选择");
        return 0;
    }

    case WM_TIMER:
        if (wParam == IDT_FLUSH) FlushOutputBuffer();
        return 0;

    case WM_NOTIFY:
    {
        LPNMHDR nmhdr = (LPNMHDR)lParam;
        if (nmhdr->idFrom == IDC_TAB && nmhdr->code == TCN_SELCHANGE)
        {
            int sel = TabCtrl_GetCurSel(g_hTab);
            ShowTab(sel);
        }
        break;
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
                    if (Lang::Load(PROG_NAME, newLang))
                    {
                        ApplyLanguage();
                    }
                }
                return 0;
            }
            case 3002: 
                ShowAboutDialog(g_hWnd);
                return 0;
            case IDC_RUNBTN:
            {
                std::wstring workDir, cmd;
                switch (g_curTab)
                {
                case 0: cmd = BuildImgGenCommand(workDir); break;
                case 1: cmd = BuildPatchCommand(workDir); break;
                case 2: cmd = BuildUpdateCommand(workDir); break;
                case 3: cmd = BuildBcdCommand(workDir); break;
                }
                if (!cmd.empty()) StartExec(workDir, cmd);
                return 0;
            }
            
            case IDC_IMG_TOOLBTN:
            {
                std::wstring file = BrowseForExe(hWnd, L"imggen.cmd");
                if (!file.empty()) SetWindowTextW(g_hImgToolEdit, file.c_str());
                return 0;
            }
            case IDC_IMG_OUTBTN:
            {
                std::wstring folder = BrowseForFolder(hWnd, L"选择输出目录");
                if (folder.empty()) return 0;
                SetWindowTextW(g_hImgOutDir, folder.c_str());

                DWORD attr = GetFileAttributesW(folder.c_str());
                if (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY))
                {
                    if (!IsDirEmpty(folder))
                    {
                        MessageBoxW(hWnd, L"输出目录非空, 请选择 FFU 文件保存位置", L"提示", MB_OK | MB_ICONINFORMATION);
                        std::wstring ffuName = GetEditText(g_hImgFfuName);
                        if (ffuName.empty()) ffuName = L"flash.ffu";
                        std::wstring savePath = SaveFfuDialog(hWnd, folder, ffuName);
                        if (!savePath.empty())
                        {
                            size_t pos = savePath.find_last_of(L"\\/");
                            if (pos != std::wstring::npos)
                            {
                                SetWindowTextW(g_hImgOutDir, savePath.substr(0, pos).c_str());
                                SetWindowTextW(g_hImgFfuName, savePath.substr(pos + 1).c_str());
                            }
                        }
                    }
                }
                return 0;
            }
            case IDC_IMG_XMLBTN:
            {
                std::wstring file = BrowseForFile(hWnd, L"选择 OEMInput XML", L"XML Files\0*.xml\0All Files\0*.*\0");
                if (!file.empty()) SetWindowTextW(g_hImgXmlEdit, file.c_str());
                return 0;
            }
            case IDC_IMG_PKGBTN:
            {
                std::wstring folder = BrowseForFolder(hWnd, L"选择 MSPackage 根目录");
                if (!folder.empty()) SetWindowTextW(g_hImgPkgEdit, folder.c_str());
                return 0;
            }
            
            case IDC_PAT_TOOLBTN:
            {
                std::wstring file = BrowseForExe(hWnd, L"imageapp.exe");
                if (!file.empty()) SetWindowTextW(g_hPatToolEdit, file.c_str());
                return 0;
            }
            case IDC_PAT_FFUBTN:
            {
                std::wstring file = BrowseForFile(hWnd, L"选择 FFU 文件", L"FFU Files\0*.ffu\0All Files\0*.*\0");
                if (!file.empty()) SetWindowTextW(g_hPatFfuEdit, file.c_str());
                return 0;
            }
            case IDC_PAT_DRVBTN:
            {
                std::wstring folder = BrowseForFolder(hWnd, L"选择驱动目录");
                if (!folder.empty()) SetWindowTextW(g_hPatDrvEdit, folder.c_str());
                return 0;
            }
            
            case IDC_UPD_TOOLBTN:
            {
                std::wstring file = BrowseForExe(hWnd, L"UpdateApp.exe");
                if (!file.empty()) SetWindowTextW(g_hUpdToolEdit, file.c_str());
                return 0;
            }
            case IDC_UPD_VHDBTN:
            {
                std::wstring file = BrowseForFile(hWnd, L"选择 VHD 文件", L"VHD Files\0*.vhd;*.vhdx\0All Files\0*.*\0");
                if (!file.empty()) SetWindowTextW(g_hUpdVhdEdit, file.c_str());
                return 0;
            }
            case IDC_UPD_CABBTN:
            {
                std::wstring folder = BrowseForFolder(hWnd, L"选择 CAB 文件夹");
                if (!folder.empty()) SetWindowTextW(g_hUpdCabEdit, folder.c_str());
                return 0;
            }
            
            case IDC_BCD_BCDBTN:
            {
                std::wstring file = BrowseForFile(hWnd, L"选择 BCD 文件", L"BCD Files\0BCD\0All Files\0*.*\0");
                if (!file.empty()) SetWindowTextW(g_hBcdEdit, file.c_str());
                return 0;
            }
            }
        }
        break;
    }

    case WM_USER + 200:
    {
        FlushOutputBuffer();
        g_isRunning = false;
        EnableWindow(g_hRunBtn, TRUE);
        DWORD exitCode = (DWORD)wParam;
        if (exitCode == 0)
        {
            SetStatus(L"执行成功");
            AppendOutput(L"\r\n=== 执行成功 ===\r\n");
        }
        else
        {
            SetStatus(L"执行失败 (退出码: " + std::to_wstring(exitCode) + L")");
            AppendOutput(L"\r\n=== 执行失败, 退出码: " + std::to_wstring(exitCode) + L" ===\r\n");
        }
        if (g_hWorkerThread) { CloseHandle(g_hWorkerThread); g_hWorkerThread = NULL; }
        return 0;
    }

    case WM_DESTROY:
        KillTimer(hWnd, IDT_FLUSH);
        FlushOutputBuffer();
        DeleteCriticalSection(&g_outputLock);
        if (g_hFont) DeleteObject(g_hFont);
        if (g_hWorkerThread) { WaitForSingleObject(g_hWorkerThread, 3000); CloseHandle(g_hWorkerThread); }
        PostQuitMessage(0);
        return 0;
    }

    return DefWindowProcW(hWnd, msg, wParam, lParam);
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nCmdShow)
{
    g_hInst = hInstance;
    SetProcessDPIAware();

    INITCOMMONCONTROLSEX icex = {};
    icex.dwSize = sizeof(icex);
    icex.dwICC = ICC_TAB_CLASSES | ICC_PROGRESS_CLASS;
    InitCommonControlsEx(&icex);

    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = L"WCOSStageToolWindow";
    wc.hIcon = LoadIcon(NULL, IDI_APPLICATION);

    if (!RegisterClassExW(&wc))
    {
        MessageBoxW(NULL, L"窗口类注册失败", L"错误", MB_OK | MB_ICONERROR);
        return 1;
    }

    RECT rc = { 0, 0, 700, 590 };
    AdjustWindowRectEx(&rc, WS_OVERLAPPEDWINDOW & ~WS_THICKFRAME & ~WS_MAXIMIZEBOX, FALSE, 0);

    g_hWnd = CreateWindowExW(0, L"WCOSStageToolWindow", L"wcosstagetool - WCOS 阶段工具",
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
