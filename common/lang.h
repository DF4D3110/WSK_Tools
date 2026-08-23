

#pragma once
#include <windows.h>
#include <string>
#include <vector>

namespace Lang
{
    struct LanguageInfo
    {
        std::wstring code;  
        std::wstring name;  
    };

    
    bool Load(const std::wstring& progName, const std::wstring& langCode);

    
    std::wstring GetStr(UINT id, const wchar_t* fallback = L"");

    
    std::vector<LanguageInfo> EnumAvailable(const std::wstring& progName);

    
    std::wstring ShowDialog(HWND parent, const std::wstring& progName);

    
    std::wstring GetCurrent();

    
    std::wstring GetLanguageName(const std::wstring& code);

    
    void Unload();
}
