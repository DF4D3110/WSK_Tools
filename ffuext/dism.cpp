#include "dism.h"
#include "disk.h"
#include <vector>
#include <sstream>
#include <algorithm>




static bool RunProcessCaptureOutput(
    const std::wstring& commandLine,
    std::wstring& output,
    DWORD& exitCode)
{
    output.clear();
    exitCode = 0;

    HANDLE hReadPipe = NULL;
    HANDLE hWritePipe = NULL;

    SECURITY_ATTRIBUTES sa = {};
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;
    sa.lpSecurityDescriptor = NULL;

    if (!CreatePipe(&hReadPipe, &hWritePipe, &sa, 0))
        return false;

    
    SetHandleInformation(hReadPipe, HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOW si = {};
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;
    si.hStdOutput = hWritePipe;
    si.hStdError = hWritePipe;

    PROCESS_INFORMATION pi = {};

    std::vector<wchar_t> cmdBuf(commandLine.begin(), commandLine.end());
    cmdBuf.push_back(L'\0');

    BOOL ok = CreateProcessW(
        NULL, cmdBuf.data(), NULL, NULL, TRUE,
        CREATE_NO_WINDOW, NULL, NULL, &si, &pi);

    CloseHandle(hWritePipe);

    if (!ok)
    {
        CloseHandle(hReadPipe);
        return false;
    }

    
    std::string rawOutput;
    char buffer[4096];
    DWORD bytesRead = 0;
    while (ReadFile(hReadPipe, buffer, sizeof(buffer), &bytesRead, NULL) && bytesRead > 0)
    {
        rawOutput.append(buffer, bytesRead);
    }

    WaitForSingleObject(pi.hProcess, INFINITE);
    GetExitCodeProcess(pi.hProcess, &exitCode);

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    CloseHandle(hReadPipe);

    
    if (!rawOutput.empty())
    {
        int wideLen = MultiByteToWideChar(CP_OEMCP, 0, rawOutput.c_str(), (int)rawOutput.size(), NULL, 0);
        if (wideLen > 0)
        {
            std::vector<wchar_t> wideBuf(wideLen);
            MultiByteToWideChar(CP_OEMCP, 0, rawOutput.c_str(), (int)rawOutput.size(), wideBuf.data(), wideLen);
            output.assign(wideBuf.begin(), wideBuf.end());
        }
    }

    return true;
}






static int ParseProgress(const std::wstring& line)
{
    size_t pctPos = line.find(L'%');
    if (pctPos == std::wstring::npos)
        return -1;

    
    size_t start = pctPos;
    while (start > 0)
    {
        wchar_t c = line[start - 1];
        if ((c >= L'0' && c <= L'9') || c == L'.')
            start--;
        else
            break;
    }

    if (start == pctPos)
        return -1;

    std::wstring numStr = line.substr(start, pctPos - start);
    try
    {
        double val = std::stod(numStr);
        if (val >= 0.0 && val <= 100.0)
            return (int)(val + 0.5);
    }
    catch (...)
    {
        return -1;
    }
    return -1;
}




static std::wstring ExtractErrorMessage(const std::wstring& output)
{
    std::wistringstream iss(output);
    std::wstring line;
    std::wstring errorMsg;
    bool inError = false;

    while (std::getline(iss, line))
    {
        
        if (!line.empty() && line.back() == L'\r')
            line.pop_back();

        if (line.find(L"错误:") != std::wstring::npos ||
            line.find(L"Error:") != std::wstring::npos)
        {
            inError = true;
            errorMsg = line;
            continue;
        }

        if (inError)
        {
            
            if (line.empty())
                continue;
            
            if (line.find(L"可以") == 0 || line.find(L"For ") == 0)
                break;
            if (!errorMsg.empty())
                errorMsg += L"\n";
            errorMsg += line;
        }
    }

    return errorMsg;
}




bool CheckDismFfuSupport()
{
    std::wstring output;
    DWORD exitCode = 0;

    if (!RunProcessCaptureOutput(L"dism.exe /?", output, exitCode))
        return false;

    
    return (output.find(L"Apply-Ffu") != std::wstring::npos ||
            output.find(L"apply-ffu") != std::wstring::npos);
}




std::wstring GetDismVersion()
{
    std::wstring output;
    DWORD exitCode = 0;

    if (!RunProcessCaptureOutput(L"dism.exe /?", output, exitCode))
        return L"未知";

    std::wistringstream iss(output);
    std::wstring line;
    while (std::getline(iss, line))
    {
        if (!line.empty() && line.back() == L'\r')
            line.pop_back();

        
        if (line.find(L"版本:") != std::wstring::npos ||
            line.find(L"Version:") != std::wstring::npos)
        {
            
            size_t colon = line.find(L':');
            if (colon != std::wstring::npos)
            {
                std::wstring ver = line.substr(colon + 1);
                
                size_t s = ver.find_first_not_of(L" \t");
                size_t e = ver.find_last_not_of(L" \t");
                if (s != std::wstring::npos)
                    return ver.substr(s, e - s + 1);
            }
            return line;
        }
    }
    return L"未知";
}




DismApplyResult ApplyFfu(
    const std::wstring& ffuPath,
    int driveIndex,
    DismOutputCallback callback,
    void* userData)
{
    DismApplyResult result = {};
    result.success = false;
    result.exitCode = 0;

    
    std::wstring drivePath = MakePhysicalDrivePath(driveIndex);
    std::wstringstream cmd;
    cmd << L"dism.exe /Apply-Ffu /ImageFile:\"" << ffuPath << L"\" /ApplyDrive:" << drivePath;

    std::wstring commandLine = cmd.str();

    
    HANDLE hReadPipe = NULL;
    HANDLE hWritePipe = NULL;

    SECURITY_ATTRIBUTES sa = {};
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;

    if (!CreatePipe(&hReadPipe, &hWritePipe, &sa, 0))
    {
        result.errorMessage = L"无法创建输出管道";
        return result;
    }

    SetHandleInformation(hReadPipe, HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOW si = {};
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;
    si.hStdOutput = hWritePipe;
    si.hStdError = hWritePipe;

    PROCESS_INFORMATION pi = {};

    std::vector<wchar_t> cmdBuf(commandLine.begin(), commandLine.end());
    cmdBuf.push_back(L'\0');

    if (callback)
        callback(L"> " + commandLine, -1, userData);

    BOOL ok = CreateProcessW(
        NULL, cmdBuf.data(), NULL, NULL, TRUE,
        CREATE_NO_WINDOW, NULL, NULL, &si, &pi);

    CloseHandle(hWritePipe);

    if (!ok)
    {
        DWORD err = GetLastError();
        result.errorMessage = L"无法启动 DISM 进程 (错误码: 0x" + 
            std::to_wstring(err) + L")";
        CloseHandle(hReadPipe);
        return result;
    }

    
    std::string rawOutput;
    char buffer[4096];
    DWORD bytesRead = 0;
    std::string lineBuffer;

    while (ReadFile(hReadPipe, buffer, sizeof(buffer), &bytesRead, NULL) && bytesRead > 0)
    {
        rawOutput.append(buffer, bytesRead);
        lineBuffer.append(buffer, bytesRead);

        
        size_t newlinePos;
        while ((newlinePos = lineBuffer.find('\n')) != std::string::npos)
        {
            std::string rawLine = lineBuffer.substr(0, newlinePos);
            lineBuffer.erase(0, newlinePos + 1);

            
            if (!rawLine.empty() && rawLine.back() == '\r')
                rawLine.pop_back();

            
            if (!rawLine.empty())
            {
                int wideLen = MultiByteToWideChar(CP_OEMCP, 0, rawLine.c_str(), (int)rawLine.size(), NULL, 0);
                if (wideLen > 0)
                {
                    std::vector<wchar_t> wideBuf(wideLen);
                    MultiByteToWideChar(CP_OEMCP, 0, rawLine.c_str(), (int)rawLine.size(), wideBuf.data(), wideLen);
                    std::wstring wideLine(wideBuf.begin(), wideBuf.end());

                    int progress = ParseProgress(wideLine);
                    if (callback)
                        callback(wideLine, progress, userData);
                }
            }
        }
    }

    
    if (!lineBuffer.empty())
    {
        if (!lineBuffer.empty() && lineBuffer.back() == '\r')
            lineBuffer.pop_back();
        int wideLen = MultiByteToWideChar(CP_OEMCP, 0, lineBuffer.c_str(), (int)lineBuffer.size(), NULL, 0);
        if (wideLen > 0)
        {
            std::vector<wchar_t> wideBuf(wideLen);
            MultiByteToWideChar(CP_OEMCP, 0, lineBuffer.c_str(), (int)lineBuffer.size(), wideBuf.data(), wideLen);
            std::wstring wideLine(wideBuf.begin(), wideBuf.end());
            int progress = ParseProgress(wideLine);
            if (callback)
                callback(wideLine, progress, userData);
        }
    }

    WaitForSingleObject(pi.hProcess, INFINITE);
    GetExitCodeProcess(pi.hProcess, &result.exitCode);

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    CloseHandle(hReadPipe);

    result.success = (result.exitCode == 0);

    if (!result.success)
    {
        
        if (!rawOutput.empty())
        {
            int wideLen = MultiByteToWideChar(CP_OEMCP, 0, rawOutput.c_str(), (int)rawOutput.size(), NULL, 0);
            if (wideLen > 0)
            {
                std::vector<wchar_t> wideBuf(wideLen);
                MultiByteToWideChar(CP_OEMCP, 0, rawOutput.c_str(), (int)rawOutput.size(), wideBuf.data(), wideLen);
                std::wstring fullOutput(wideBuf.begin(), wideBuf.end());
                result.errorMessage = ExtractErrorMessage(fullOutput);
            }
        }
        if (result.errorMessage.empty())
        {
            result.errorMessage = L"DISM 退出码: 0x" + std::to_wstring(result.exitCode);
        }
    }

    return result;
}
