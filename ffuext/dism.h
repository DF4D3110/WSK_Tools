#pragma once
#include <windows.h>
#include <string>


struct DismApplyResult
{
    bool         success;       
    DWORD        exitCode;      
    std::wstring errorMessage;  
};


typedef void (*DismOutputCallback)(const std::wstring& line, int progress, void* userData);



bool CheckDismFfuSupport();


std::wstring GetDismVersion();






DismApplyResult ApplyFfu(
    const std::wstring& ffuPath,
    int driveIndex,
    DismOutputCallback callback,
    void* userData);
