#pragma once
#include <windows.h>
#include <string>
#include <vector>

struct DiskInfo
{
    int         index;       
    uint64_t    sizeBytes;   
    std::wstring model;      
    bool        accessible;  
};

std::vector<DiskInfo> EnumeratePhysicalDisks(int maxIndex = 64);

std::wstring FormatDiskSize(uint64_t bytes);

std::wstring MakePhysicalDrivePath(int index);
