#include "disk.h"
#include <winioctl.h>
#include <sstream>
#include <iomanip>

std::wstring MakePhysicalDrivePath(int index)
{
    std::wstringstream ss;
    ss << L"\\\\.\\PhysicalDrive" << index;
    return ss.str();
}

std::wstring FormatDiskSize(uint64_t bytes)
{
    if (bytes == 0)
        return L"未知";

    const double KB = 1024.0;
    const double MB = KB * 1024.0;
    const double GB = MB * 1024.0;
    const double TB = GB * 1024.0;

    std::wstringstream ss;
    ss << std::fixed << std::setprecision(1);

    if (bytes >= TB)
        ss << (bytes / TB) << L" TB";
    else if (bytes >= GB)
        ss << (bytes / GB) << L" GB";
    else if (bytes >= MB)
        ss << (bytes / MB) << L" MB";
    else if (bytes >= KB)
        ss << (bytes / KB) << L" KB";
    else
        ss << bytes << L" B";

    return ss.str();
}

static bool TryOpenDisk(int index, DWORD access, HANDLE& hOut)
{
    std::wstring path = MakePhysicalDrivePath(index);
    hOut = CreateFileW(
        path.c_str(),
        access,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);
    return (hOut != INVALID_HANDLE_VALUE);
}

static uint64_t GetDiskSize(HANDLE hDisk)
{
    DISK_GEOMETRY_EX geo = {};
    DWORD bytesReturned = 0;
    if (DeviceIoControl(hDisk, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
        NULL, 0, &geo, sizeof(geo), &bytesReturned, NULL))
    {
        return geo.DiskSize.QuadPart;
    }
    return 0;
}

static std::wstring GetDiskModel(HANDLE hDisk)
{
    STORAGE_PROPERTY_QUERY query = {};
    query.PropertyId = StorageDeviceProperty;
    query.QueryType = PropertyStandardQuery;

    BYTE buffer[1024] = {};
    DWORD bytesReturned = 0;

    if (DeviceIoControl(hDisk, IOCTL_STORAGE_QUERY_PROPERTY,
        &query, sizeof(query), buffer, sizeof(buffer), &bytesReturned, NULL))
    {
        STORAGE_DEVICE_DESCRIPTOR* desc = (STORAGE_DEVICE_DESCRIPTOR*)buffer;
        if (desc->ProductIdOffset != 0)
        {
            char* productId = (char*)(buffer + desc->ProductIdOffset);
            
            std::string s(productId);
            while (!s.empty() && (s.back() == ' ' || s.back() == '\0'))
                s.pop_back();
            if (!s.empty())
            {
                std::wstring ws(s.begin(), s.end());
                return ws;
            }
        }
    }
    return L"";
}

std::vector<DiskInfo> EnumeratePhysicalDisks(int maxIndex)
{
    std::vector<DiskInfo> result;

    for (int i = 0; i < maxIndex; i++)
    {
        
        HANDLE hCheck = INVALID_HANDLE_VALUE;
        if (!TryOpenDisk(i, 0, hCheck))
        {
            
            continue;
        }
        CloseHandle(hCheck);

        DiskInfo info = {};
        info.index = i;
        info.sizeBytes = 0;
        info.accessible = false;

        HANDLE hRead = INVALID_HANDLE_VALUE;
        if (TryOpenDisk(i, GENERIC_READ, hRead))
        {
            info.accessible = true;
            info.sizeBytes = GetDiskSize(hRead);
            info.model = GetDiskModel(hRead);
            CloseHandle(hRead);
        }

        result.push_back(info);
    }

    return result;
}
