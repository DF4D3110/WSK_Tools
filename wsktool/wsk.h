#pragma once
#include <windows.h>
#include <string>
#include <vector>

struct WskBuildParams
{
    std::wstring wskRoot;       
    std::wstring workspace;     
    std::wstring product;       
    std::wstring architecture;  
    bool         isVM;          
};

struct WskBuildResult
{
    bool         success;
    DWORD        exitCode;
    std::wstring errorMessage;
    std::wstring outputFilePath; 
    std::wstring outputFolder;   
};

typedef void (*WskOutputCallback)(const std::wstring& line, void* userData);

std::wstring DetectWskLocation();

bool IsValidWskRoot(const std::wstring& path);

std::wstring GetWskVersion(const std::wstring& wskRoot);

std::wstring GuiArchToPrepArch(const std::wstring& guiArch);

std::wstring PrepArchToFmFolder(const std::wstring& prepArch);

std::vector<std::wstring> EnumerateProducts(const std::wstring& wskRoot, const std::wstring& prepArch);

WskBuildResult RunWskBuild(const WskBuildParams& params, WskOutputCallback callback, void* userData);

WskBuildResult RunWskPrep(const WskBuildParams& params, WskOutputCallback callback, void* userData);

WskBuildResult RunWskBuildImage(const WskBuildParams& params, const std::wstring& xmlPath,
                                  WskOutputCallback callback, void* userData);

std::vector<std::wstring> EnumerateWorkspaceXml(const std::wstring& workspace);

std::wstring FindOutputImage(const std::wstring& workspace);
