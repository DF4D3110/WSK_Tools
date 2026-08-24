#include "wsk.h"
#include <vector>
#include <sstream>
#include <fstream>
#include <algorithm>

static bool FileExists(const std::wstring& path)
{
    DWORD attr = GetFileAttributesW(path.c_str());
    return (attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY));
}

static bool DirExists(const std::wstring& path)
{
    DWORD attr = GetFileAttributesW(path.c_str());
    return (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY));
}

static std::wstring EnsureTrailingBackslash(const std::wstring& path)
{
    if (path.empty())
        return path;
    if (path.back() == L'\\' || path.back() == L'/')
        return path;
    return path + L'\\';
}

std::wstring DetectWskLocation()
{
    DWORD drives = GetLogicalDrives();
    std::vector<std::wstring> cdromDrives;
    std::vector<std::wstring> allDrives;

    for (int i = 0; i < 26; i++)
    {
        if (!(drives & (1 << i)))
            continue;

        wchar_t driveRoot[4] = { (wchar_t)(L'A' + i), L':', L'\\', L'\0' };
        UINT type = GetDriveTypeW(driveRoot);

        std::wstring root(driveRoot);

        if (type == DRIVE_CDROM)
            cdromDrives.push_back(root);
        allDrives.push_back(root);
    }

    for (const auto& root : cdromDrives)
    {
        if (FileExists(root + L"SetImagGenEnv.cmd"))
            return root;
    }

    for (const auto& root : allDrives)
    {
        if (FileExists(root + L"SetImagGenEnv.cmd"))
            return root;
    }

    return L"";
}

bool IsValidWskRoot(const std::wstring& path)
{
    std::wstring p = EnsureTrailingBackslash(path);
    return FileExists(p + L"SetImagGenEnv.cmd");
}

std::wstring GetWskVersion(const std::wstring& wskRoot)
{
    std::wstring p = EnsureTrailingBackslash(wskRoot) + L"Version.txt";
    if (!FileExists(p))
        return L"未知";

    std::wifstream file(p);
    if (!file.is_open())
        return L"未知";

    std::wstring line;
    std::getline(file, line);
    file.close();

    size_t s = line.find_first_not_of(L" \t\r\n");
    size_t e = line.find_last_not_of(L" \t\r\n");
    if (s != std::wstring::npos)
        return line.substr(s, e - s + 1);
    return L"未知";
}

std::wstring GuiArchToPrepArch(const std::wstring& guiArch)
{
    
    if (guiArch == L"x86")   return L"x86";
    if (guiArch == L"amd64") return L"AMD64";
    if (guiArch == L"arm32") return L"Arm";
    if (guiArch == L"arm64") return L"Arm64";
    return guiArch; 
}

std::wstring PrepArchToFmFolder(const std::wstring& prepArch)
{
    
    if (prepArch == L"x86")   return L"x86";
    if (prepArch == L"AMD64") return L"amd64";
    if (prepArch == L"Arm")   return L"arm";
    if (prepArch == L"Arm64") return L"arm64";

    std::wstring lower = prepArch;
    std::transform(lower.begin(), lower.end(), lower.begin(), ::towlower);
    return lower;
}

std::vector<std::wstring> EnumerateProducts(const std::wstring& wskRoot, const std::wstring& prepArch)
{
    std::vector<std::wstring> result;

    std::wstring fmFolder = PrepArchToFmFolder(prepArch);
    std::wstring path = EnsureTrailingBackslash(wskRoot) +
        L"Program Files\\Windows Kits\\10\\FMFiles\\" + fmFolder + L"\\";

    if (!DirExists(path))
        return result;

    WIN32_FIND_DATAW fd = {};
    std::wstring searchPath = path + L"*";
    HANDLE hFind = FindFirstFileW(searchPath.c_str(), &fd);

    if (hFind == INVALID_HANDLE_VALUE)
        return result;

    do
    {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
        {
            std::wstring name(fd.cFileName);
            if (name != L"." && name != L"..")
                result.push_back(name);
        }
    } while (FindNextFileW(hFind, &fd));

    FindClose(hFind);
    return result;
}

static std::wstring BuildPrepBatchContent(const WskBuildParams& params)
{
    std::wstring wskRoot = EnsureTrailingBackslash(params.wskRoot);
    std::wstring vmFlag = params.isVM ? L"-VM" : L"";
    std::wstring machineType = params.isVM ? L"Virtual Machine (VM)" : L"Physical Machine";

    std::wstringstream ss;
    ss << L"@echo on\r\n";
    ss << L"chcp 65001 >nul\r\n";
    ss << L"setlocal enabledelayedexpansion\r\n";
    ss << L"\r\n";
    ss << L"cd /d \"" << wskRoot << L"\"\r\n";
    ss << L"\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"echo  WSK Prep - wsktool\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"echo  WSK Root:     " << params.wskRoot << L"\r\n";
    ss << L"echo  Workspace:    " << params.workspace << L"\r\n";
    ss << L"echo  Product:      " << params.product << L"\r\n";
    ss << L"echo  Architecture: " << params.architecture << L"\r\n";
    ss << L"echo  Machine Type: " << machineType << L"\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"echo.\r\n";
    ss << L"\r\n";
    ss << L"echo [1/2] Setting up WSK image generation environment...\r\n";
    ss << L"echo [CMD] call \"" << wskRoot << L"BuildEnv\\SetupWSKEnv.cmd\"\r\n";
    ss << L"call \"" << wskRoot << L"BuildEnv\\SetupWSKEnv.cmd\"\r\n";
    ss << L"@echo on\r\n";
    ss << L"echo [INFO] SetupWSKEnv exit code: %errorlevel%\r\n";
    ss << L"echo [INFO] WSKContentRoot: %WSKContentRoot%\r\n";
    ss << L"if %errorlevel% neq 0 (\r\n";
    ss << L"    echo [ERROR] SetupWSKEnv failed with exit code %errorlevel%\r\n";
    ss << L"    exit /b %errorlevel%\r\n";
    ss << L")\r\n";
    ss << L"echo.\r\n";
    ss << L"\r\n";
    ss << L"echo [2/2] Preparing workspace with PrepWSKWorkspace...\r\n";
    ss << L"echo [CMD] call PrepWSKWorkspace \"" << params.workspace << L"\" -Product " << params.product
       << L" -Architecture " << params.architecture << L" " << vmFlag << L" -Overwrite:Yes\r\n";
    ss << L"call PrepWSKWorkspace \"" << params.workspace << L"\" -Product " << params.product
       << L" -Architecture " << params.architecture << L" " << vmFlag << L" -Overwrite:Yes\r\n";
    ss << L"@echo on\r\n";
    ss << L"echo [INFO] PrepWSKWorkspace exit code: %errorlevel%\r\n";
    ss << L"if %errorlevel% neq 0 (\r\n";
    ss << L"    echo [ERROR] PrepWSKWorkspace failed with exit code %errorlevel%\r\n";
    ss << L"    exit /b %errorlevel%\r\n";
    ss << L")\r\n";
    ss << L"echo.\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"echo  WSK Prep Completed Successfully\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"endlocal\r\n";
    ss << L"exit /b 0\r\n";

    return ss.str();
}

static std::wstring BuildImageBatchContent(const WskBuildParams& params, const std::wstring& xmlPath)
{
    std::wstring wskRoot = EnsureTrailingBackslash(params.wskRoot);

    std::wstringstream ss;
    ss << L"@echo on\r\n";
    ss << L"chcp 65001 >nul\r\n";
    ss << L"setlocal enabledelayedexpansion\r\n";
    ss << L"\r\n";
    ss << L"cd /d \"" << wskRoot << L"\"\r\n";
    ss << L"\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"echo  WSK Build Image - wsktool\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"echo  WSK Root:  " << params.wskRoot << L"\r\n";
    ss << L"echo  XML Input: " << xmlPath << L"\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"echo.\r\n";
    ss << L"\r\n";
    ss << L"echo [1/2] Setting up WSK image generation environment...\r\n";
    ss << L"echo [CMD] call \"" << wskRoot << L"BuildEnv\\SetupWSKEnv.cmd\"\r\n";
    ss << L"call \"" << wskRoot << L"BuildEnv\\SetupWSKEnv.cmd\"\r\n";
    ss << L"@echo on\r\n";
    ss << L"if %errorlevel% neq 0 (\r\n";
    ss << L"    echo [ERROR] SetupWSKEnv failed with exit code %errorlevel%\r\n";
    ss << L"    exit /b %errorlevel%\r\n";
    ss << L")\r\n";
    ss << L"echo.\r\n";
    ss << L"\r\n";
    ss << L"set \"WSKWorkspaceRoot=" << params.workspace << L"\"\r\n";
    ss << L"echo [INFO] WSKWorkspaceRoot: %WSKWorkspaceRoot%\r\n";
    ss << L"echo.\r\n";
    ss << L"\r\n";
    ss << L"echo [2/2] Building image with BuildWSKImage...\r\n";
    ss << L"echo [CMD] PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File \"" << wskRoot
       << L"Program Files\\Windows Kits\\10\\Tools\\Scripts\\BuildWSKImage.ps1\" \"" << xmlPath << L"\"\r\n";
    ss << L"PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File \"" << wskRoot
       << L"Program Files\\Windows Kits\\10\\Tools\\Scripts\\BuildWSKImage.ps1\" \"" << xmlPath << L"\"\r\n";
    ss << L"echo [INFO] BuildWSKImage exit code: %errorlevel%\r\n";
    ss << L"if %errorlevel% neq 0 (\r\n";
    ss << L"    echo [ERROR] BuildWSKImage failed with exit code %errorlevel%\r\n";
    ss << L"    exit /b %errorlevel%\r\n";
    ss << L")\r\n";
    ss << L"echo.\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"echo  WSK Build Image Completed Successfully\r\n";
    ss << L"echo ============================================================\r\n";
    ss << L"endlocal\r\n";
    ss << L"exit /b 0\r\n";

    return ss.str();
}

static std::wstring ExtractErrorFromOutput(const std::wstring& output)
{
    std::wistringstream iss(output);
    std::wstring line;
    std::wstring errorMsg;

    while (std::getline(iss, line))
    {
        if (!line.empty() && line.back() == L'\r')
            line.pop_back();

        if (line.find(L"[ERROR]") != std::wstring::npos)
        {
            if (!errorMsg.empty())
                errorMsg += L"\n";
            errorMsg += line;
        }
    }

    return errorMsg;
}

static WskBuildResult ExecuteBatch(const std::wstring& batchContent,
                                     WskOutputCallback callback, void* userData)
{
    WskBuildResult result = {};
    result.success = false;
    result.exitCode = 0;

    wchar_t tempPath[MAX_PATH];
    GetTempPathW(MAX_PATH, tempPath);

    wchar_t tempFile[MAX_PATH];
    GetTempFileNameW(tempPath, L"wsk", 0, tempFile);

    std::wstring batchPath = std::wstring(tempFile) + L".bat";
    DeleteFileW(tempFile);

    {
        std::ofstream file(batchPath, std::ios::binary);
        if (!file.is_open())
        {
            result.errorMessage = L"无法创建临时批处理文件";
            return result;
        }
        int ansiLen = WideCharToMultiByte(CP_ACP, 0, batchContent.c_str(), -1, NULL, 0, NULL, NULL);
        if (ansiLen > 0)
        {
            std::vector<char> ansiBuf(ansiLen);
            WideCharToMultiByte(CP_ACP, 0, batchContent.c_str(), -1, ansiBuf.data(), ansiLen, NULL, NULL);
            file.write(ansiBuf.data(), ansiLen - 1);
        }
        file.close();
    }

    if (callback)
        callback(L"> 临时批处理: " + batchPath, userData);

    HANDLE hReadPipe = NULL, hWritePipe = NULL;
    SECURITY_ATTRIBUTES sa = {};
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;

    if (!CreatePipe(&hReadPipe, &hWritePipe, &sa, 0))
    {
        result.errorMessage = L"无法创建输出管道";
        DeleteFileW(batchPath.c_str());
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
    std::wstring cmdLine = L"cmd.exe /c \"" + batchPath + L"\"";
    std::vector<wchar_t> cmdBuf(cmdLine.begin(), cmdLine.end());
    cmdBuf.push_back(L'\0');

    BOOL ok = CreateProcessW(NULL, cmdBuf.data(), NULL, NULL, TRUE,
                              CREATE_NO_WINDOW, NULL, NULL, &si, &pi);
    CloseHandle(hWritePipe);

    if (!ok)
    {
        DWORD err = GetLastError();
        result.errorMessage = L"无法启动 cmd.exe (错误码: 0x" + std::to_wstring(err) + L")";
        CloseHandle(hReadPipe);
        DeleteFileW(batchPath.c_str());
        return result;
    }

    std::string rawOutput;
    std::string lineBuffer;
    char buffer[4096];
    DWORD bytesRead = 0;

    auto processLines = [&](const char* data, DWORD len) {
        rawOutput.append(data, len);
        lineBuffer.append(data, len);
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
                    if (callback) callback(wideLine, userData);
                }
            }
            else if (callback) callback(L"", userData);
        }
    };

    while (true)
    {
        
        DWORD waitResult = WaitForSingleObject(pi.hProcess, 100);

        DWORD bytesAvailable = 0;
        BOOL peekOk = PeekNamedPipe(hReadPipe, NULL, 0, NULL, &bytesAvailable, NULL);

        if (peekOk && bytesAvailable > 0)
        {
            if (ReadFile(hReadPipe, buffer, sizeof(buffer), &bytesRead, NULL) && bytesRead > 0)
            {
                processLines(buffer, bytesRead);
            }
            continue; 
        }

        if (waitResult == WAIT_OBJECT_0)
        {
            
            while (PeekNamedPipe(hReadPipe, NULL, 0, NULL, &bytesAvailable, NULL) && bytesAvailable > 0)
            {
                if (ReadFile(hReadPipe, buffer, sizeof(buffer), &bytesRead, NULL) && bytesRead > 0)
                    processLines(buffer, bytesRead);
                else
                    break;
            }
            break;
        }
        
    }

    if (!lineBuffer.empty())
    {
        if (lineBuffer.back() == '\r') lineBuffer.pop_back();
        if (!lineBuffer.empty())
        {
            int wideLen = MultiByteToWideChar(CP_OEMCP, 0, lineBuffer.c_str(), (int)lineBuffer.size(), NULL, 0);
            if (wideLen > 0)
            {
                std::vector<wchar_t> wideBuf(wideLen);
                MultiByteToWideChar(CP_OEMCP, 0, lineBuffer.c_str(), (int)lineBuffer.size(), wideBuf.data(), wideLen);
                std::wstring wideLine(wideBuf.begin(), wideBuf.end());
                if (callback) callback(wideLine, userData);
            }
        }
    }

    GetExitCodeProcess(pi.hProcess, &result.exitCode);
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    CloseHandle(hReadPipe);
    DeleteFileW(batchPath.c_str());

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
                result.errorMessage = ExtractErrorFromOutput(fullOutput);
            }
        }
        if (result.errorMessage.empty())
            result.errorMessage = L"构建失败, 退出码: 0x" + std::to_wstring(result.exitCode);
    }

    return result;
}

WskBuildResult RunWskPrep(const WskBuildParams& params, WskOutputCallback callback, void* userData)
{
    std::wstring batchContent = BuildPrepBatchContent(params);
    return ExecuteBatch(batchContent, callback, userData);
}

WskBuildResult RunWskBuildImage(const WskBuildParams& params, const std::wstring& xmlPath,
                                  WskOutputCallback callback, void* userData)
{
    std::wstring batchContent = BuildImageBatchContent(params, xmlPath);
    WskBuildResult result = ExecuteBatch(batchContent, callback, userData);

    if (result.success)
    {
        result.outputFilePath = FindOutputImage(params.workspace);
        if (!result.outputFilePath.empty())
        {
            size_t lastSlash = result.outputFilePath.find_last_of(L"\\/");
            if (lastSlash != std::wstring::npos)
                result.outputFolder = result.outputFilePath.substr(0, lastSlash);
        }
    }
    return result;
}

WskBuildResult RunWskBuild(const WskBuildParams& params, WskOutputCallback callback, void* userData)
{
    WskBuildResult prepResult = RunWskPrep(params, callback, userData);
    if (!prepResult.success)
        return prepResult;

    std::vector<std::wstring> xmlFiles = EnumerateWorkspaceXml(params.workspace);
    if (xmlFiles.empty())
    {
        WskBuildResult r = {};
        r.success = false;
        r.errorMessage = L"工作区中未找到 XML 文件";
        return r;
    }

    std::wstring selectedXml;
    for (const auto& f : xmlFiles)
    {
        if (f.find(L"_Configuration.xml") == std::wstring::npos)
        {
            selectedXml = f;
            break;
        }
    }
    if (selectedXml.empty())
        selectedXml = xmlFiles[0];

    return RunWskBuildImage(params, selectedXml, callback, userData);
}

std::vector<std::wstring> EnumerateWorkspaceXml(const std::wstring& workspace)
{
    std::vector<std::wstring> result;
    std::wstring searchPath = workspace + L"\\*.xml";

    WIN32_FIND_DATAW fd = {};
    HANDLE hFind = FindFirstFileW(searchPath.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE)
        return result;

    do
    {
        if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
        {
            result.push_back(workspace + L"\\" + fd.cFileName);
        }
    } while (FindNextFileW(hFind, &fd));

    FindClose(hFind);
    return result;
}

static std::wstring FindImageInDir(const std::wstring& dir, int maxDepth, FILETIME& newestTime)
{
    std::wstring result;
    if (maxDepth < 0) return result;

    const wchar_t* extensions[] = { L"*.ffu", L"*.vhdx" };

    for (const wchar_t* ext : extensions)
    {
        std::wstring searchPath = EnsureTrailingBackslash(dir) + ext;
        WIN32_FIND_DATAW fd = {};
        HANDLE hFind = FindFirstFileW(searchPath.c_str(), &fd);
        if (hFind == INVALID_HANDLE_VALUE) continue;

        do
        {
            if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
            {
                if (CompareFileTime(&fd.ftLastWriteTime, &newestTime) > 0)
                {
                    newestTime = fd.ftLastWriteTime;
                    result = EnsureTrailingBackslash(dir) + fd.cFileName;
                }
            }
        } while (FindNextFileW(hFind, &fd));
        FindClose(hFind);
    }

    std::wstring dirSearch = EnsureTrailingBackslash(dir) + L"*";
    WIN32_FIND_DATAW fd = {};
    HANDLE hFind = FindFirstFileW(dirSearch.c_str(), &fd);
    if (hFind != INVALID_HANDLE_VALUE)
    {
        do
        {
            if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) &&
                wcscmp(fd.cFileName, L".") != 0 && wcscmp(fd.cFileName, L"..") != 0)
            {
                std::wstring subDir = EnsureTrailingBackslash(dir) + fd.cFileName;
                std::wstring found = FindImageInDir(subDir, maxDepth - 1, newestTime);
                if (!found.empty()) result = found;
            }
        } while (FindNextFileW(hFind, &fd));
        FindClose(hFind);
    }

    return result;
}

std::wstring FindOutputImage(const std::wstring& workspace)
{
    FILETIME newestTime = {};
    std::wstring result;

    result = FindImageInDir(workspace, 3, newestTime);

    std::wstring outputDir = workspace + L".Output";
    std::wstring found = FindImageInDir(outputDir, 3, newestTime);
    if (!found.empty()) result = found;

    return result;
}
