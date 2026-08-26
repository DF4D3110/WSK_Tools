#include <windows.h>
#include <guiddef.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <stdio.h>

typedef void (__stdcall *GetIUPropertyStore_t)(void** outStore);
typedef void (__stdcall *CreateNewPool_t)(void* store, GUID* outPoolId);
typedef void (__stdcall *CreateNewSpace_t)(void* store);
typedef void (__stdcall *CheckSpacesVersion_t)();

#pragma pack(push, 4)
struct PropertyValue {
    void* unknown0;
    void* unknown4;
    int type;
    unsigned int propId;
    void* dataPtr;
    int dataSize;
};
#pragma pack(pop)

typedef void (__fastcall *SetProperty_t)(void* thisPtr, PropertyValue* pv);

static HMODULE g_hIUSpaces = NULL;

static void DbgLog(const char* fmt, ...);

static int EnsureLoaded() {
    if (!g_hIUSpaces) g_hIUSpaces = LoadLibraryW(L"IUSpaces_nocfg.dll");
    return g_hIUSpaces ? 0 : -1;
}

static void* GetSubObj2(void* store) {
    return (char*)store + 0x40;
}

static SetProperty_t GetSetProperty(void* store) {
    void* subObj = GetSubObj2(store);
    void** vtable = *(void***)subObj;
    return (SetProperty_t)vtable[3];
}

static void SetPropBlob(void* store, unsigned int propId, const void* data, int size) {
    SetProperty_t setProp = GetSetProperty(store);
    PropertyValue pv;
    pv.unknown0 = NULL;
    pv.unknown4 = NULL;
    pv.type = 2;
    pv.propId = propId;
    pv.dataPtr = (void*)data;
    pv.dataSize = size;
    setProp(GetSubObj2(store), &pv);
}

static void SetPropTypeBlob(void* store, unsigned int propId, int type, const void* data, int size) {
    DbgLog("[SetProp] entering, store=%p type=%d propId=%d\n", store, type, propId);
    void* subObj = (char*)store + 0x40;
    DbgLog("[SetProp] subObj2=%p\n", subObj);
    void** vtable = *(void***)subObj;
    DbgLog("[SetProp] vtable=%p\n", vtable);
    if (!vtable) { DbgLog("[SetProp] vtable is NULL!\n"); return; }
    SetProperty_t setProp = (SetProperty_t)vtable[3];
    DbgLog("[SetProp] setProp=%p\n", setProp);
    if (!setProp) { DbgLog("[SetProp] setProp is NULL!\n"); return; }
    PropertyValue pv;
    pv.unknown0 = NULL;
    pv.unknown4 = NULL;
    pv.type = type;
    pv.propId = propId;
    pv.dataPtr = (void*)data;
    pv.dataSize = size;
    DbgLog("[SetProp] calling setProp(subObj=%p, pv=%p)\n", subObj, &pv);
    setProp(subObj, &pv);
    DbgLog("[SetProp] setProp returned OK\n");
}

static void SetPropWString(void* store, unsigned int propId, const wchar_t* str) {
    int len = (int)(wcslen(str) + 1) * sizeof(wchar_t);
    SetPropBlob(store, propId, str, len);
}

static void SetPropGuid(void* store, unsigned int propId, const GUID* guid) {
    SetPropBlob(store, propId, guid, sizeof(GUID));
}

static void SetPropULong(void* store, unsigned int propId, unsigned long long value) {
    SetPropTypeBlob(store, propId, 5, &value, sizeof(unsigned long long));
}

extern "C" __declspec(dllexport) int __stdcall IUSpaces_CheckVersion() {
    if (EnsureLoaded()) return -1;
    CheckSpacesVersion_t fn = (CheckSpacesVersion_t)GetProcAddress(g_hIUSpaces, "CheckSpacesVersion");
    if (!fn) return -2;
    __try { fn(); return 0; }
    __except(EXCEPTION_EXECUTE_HANDLER) { return -3; }
}

extern "C" __declspec(dllexport) int __stdcall IUSpaces_CreatePool(
    const wchar_t* poolName, const wchar_t* diskPath, GUID* outPoolId)
{
    if (EnsureLoaded()) return -1;
    GetIUPropertyStore_t getStore = (GetIUPropertyStore_t)GetProcAddress(g_hIUSpaces, "GetIUPropertyStore");
    CreateNewPool_t createPool = (CreateNewPool_t)GetProcAddress(g_hIUSpaces, "CreateNewPool");
    if (!getStore || !createPool) return -2;
    void* store = NULL;
    getStore(&store);
    if (!store) return -3;

    int zeroVal = 0;
    SetPropTypeBlob(store, 0, 4, &zeroVal, 4);
    SetPropWString(store, 0, poolName);
    unsigned long long poolSize = 1ULL * 1024 * 1024 * 1024;
    SetPropULong(store, 1, poolSize);
    unsigned int flagsVal = 1;
    SetPropTypeBlob(store, 5, 4, &flagsVal, 4);

    void* subObj = (char*)store + 0x40;
    __try {
        createPool(subObj, outPoolId);
        return 0;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) { return -4; }
}

extern "C" __declspec(dllexport) int __stdcall IUSpaces_CreateSpace(
    const GUID* poolId, const wchar_t* spaceName, unsigned long long sizeBytes)
{
    if (EnsureLoaded()) return -1;
    GetIUPropertyStore_t getStore = (GetIUPropertyStore_t)GetProcAddress(g_hIUSpaces, "GetIUPropertyStore");
    CreateNewSpace_t createSpace = (CreateNewSpace_t)GetProcAddress(g_hIUSpaces, "CreateNewSpace");
    if (!getStore || !createSpace) return -2;
    void* store = NULL;
    getStore(&store);
    if (!store) return -3;
    __try {
        SetPropGuid(store, 2, poolId);
        SetPropWString(store, 0, spaceName);
        SetPropULong(store, 1, sizeBytes);
        createSpace(store);
        return 0;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) { return -4; }
}

static void DbgLog(const char* fmt, ...) {
    char buf[1024];
    va_list args;
    va_start(args, fmt);
    _vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);
    OutputDebugStringA(buf);
    char path[MAX_PATH];
    GetTempPathA(MAX_PATH, path);
    strcat(path, "iuspaces_wrapper.log");
    HANDLE h = CreateFileA(path, FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, NULL);
    if (h != INVALID_HANDLE_VALUE) {
        DWORD written;
        WriteFile(h, buf, (DWORD)strlen(buf), &written, NULL);
        CloseHandle(h);
    }
}

static void DumpStoreState(void* store) {
    if (!store) { DbgLog("[DUMP] store is NULL\n"); return; }
    unsigned char* p = (unsigned char*)store;
    DbgLog("[DUMP] store=%p\n", store);
    DbgLog("[DUMP] +0x00 (main vtable ptr): %08x\n", *(unsigned int*)p);
    DbgLog("[DUMP] +0x04: %08x\n", *(unsigned int*)(p+4));
    DbgLog("[DUMP] +0x08: %08x\n", *(unsigned int*)(p+8));
    DbgLog("[DUMP] +0x3C (subobj1): %08x\n", *(unsigned int*)(p+0x3c));
    DbgLog("[DUMP] +0x40 (subobj2): %08x\n", *(unsigned int*)(p+0x40));
    void* subObj2 = (char*)store + 0x40;
    void** vtable = *(void***)subObj2;
    DbgLog("[DUMP] subobj2 vtable=%p\n", vtable);
    if (vtable) {
        for (int i = 0; i < 8; i++) {
            DbgLog("[DUMP]   vtable[%d]=%p\n", i, vtable[i]);
        }
    }
    void* mainVt = *(void**)store;
    DbgLog("[DUMP] main vtable=%p\n", mainVt);
    if (mainVt) {
        unsigned int* mv = (unsigned int*)mainVt;
        for (int i = 0; i < 8; i++) {
            DbgLog("[DUMP]   mainvt[%d]=%08x\n", i, mv[i]);
        }
    }
}

extern "C" __declspec(dllexport) int __stdcall IUSpaces_TestProps();

#pragma optimize("", off)
extern "C" __declspec(dllexport) int __stdcall IUSpaces_DiagnosePool(
    const wchar_t* poolName, const wchar_t* diskPath, GUID* outPoolId)
{
    if (EnsureLoaded()) return -1;
    GetIUPropertyStore_t getStore = (GetIUPropertyStore_t)GetProcAddress(g_hIUSpaces, "GetIUPropertyStore");
    CreateNewPool_t createPool = (CreateNewPool_t)GetProcAddress(g_hIUSpaces, "CreateNewPool");
    if (!getStore || !createPool) return -2;
    void* store = NULL;
    getStore(&store);
    if (!store) return -3;
    DbgLog("DIAG store=%p\n", store);
    void* subObj = (char*)store + 0x40;
    void** vtable = *(void***)subObj;
    SetProperty_t setProp = (SetProperty_t)vtable[3];
    DbgLog("subObj=%p vt=%p sp=%p\n", subObj, vtable, setProp);
    int zeroVal = 0;
    PropertyValue pv4;
    pv4.unknown0 = NULL; pv4.unknown4 = NULL; pv4.type = 4; pv4.propId = 0;
    pv4.dataPtr = &zeroVal; pv4.dataSize = 4;
    DbgLog("setProp type4...\n");
    setProp(subObj, &pv4);
    DbgLog("setProp type4 OK\n");
    const wchar_t* pname = poolName ? poolName : L"TestPool";
    PropertyValue pv2;
    pv2.unknown0 = NULL; pv2.unknown4 = NULL; pv2.type = 2; pv2.propId = 0;
    pv2.dataPtr = (void*)pname; pv2.dataSize = (int)(wcslen(pname)+1)*2;
    DbgLog("setProp type2...\n");
    setProp(subObj, &pv2);
    DbgLog("setProp type2 OK\n");

    unsigned long long poolSize = 1ULL * 1024 * 1024 * 1024;
    PropertyValue pv5;
    pv5.unknown0 = NULL; pv5.unknown4 = NULL; pv5.type = 5; pv5.propId = 1;
    pv5.dataPtr = &poolSize; pv5.dataSize = 8;
    DbgLog("setProp type5 (size=%llu)...\n", poolSize);
    __try { setProp(subObj, &pv5); DbgLog("setProp type5 OK\n"); }
    __except(EXCEPTION_EXECUTE_HANDLER) { DbgLog("setProp type5 FAILED 0x%08x\n", GetExceptionCode()); }

    unsigned int flagsVal = 1;
    PropertyValue pvf;
    pvf.unknown0 = NULL; pvf.unknown4 = NULL; pvf.type = 4; pvf.propId = 5;
    pvf.dataPtr = &flagsVal; pvf.dataSize = 4;
    DbgLog("setProp type4 propId5 (flags)...\n");
    __try { setProp(subObj, &pvf); DbgLog("setProp type4 propId5 OK\n"); }
    __except(EXCEPTION_EXECUTE_HANDLER) { DbgLog("setProp type4 propId5 FAILED 0x%08x\n", GetExceptionCode()); }

    DbgLog("CreateNewPool(store=%p)...\n", store);
    __try { createPool(store, outPoolId); DbgLog("CreateNewPool OK\n"); return 0; }
    __except(EXCEPTION_EXECUTE_HANDLER) { DbgLog("CreateNewPool FAIL 0x%08x\n", GetExceptionCode()); return -4; }
}
#pragma optimize("", on)

extern "C" __declspec(dllexport) int __stdcall IUSpaces_TestProps() {
    if (EnsureLoaded()) return -1;
    GetIUPropertyStore_t getStore = (GetIUPropertyStore_t)GetProcAddress(g_hIUSpaces, "GetIUPropertyStore");
    if (!getStore) return -2;
    void* store = NULL;
    getStore(&store);
    if (!store) return -3;

    void* subObj = (char*)store + 0x40;
    void** vtable = *(void***)subObj;
    if (!vtable) return -5;

    SetProperty_t setProp = (SetProperty_t)vtable[3];
    if (!setProp) return -6;

    DbgLog("[TestProps] store=%p subObj=%p vtable=%p setProp=%p\n", store, subObj, vtable, setProp);

    int testUint = 42;
    PropertyValue pv4;
    pv4.unknown0 = NULL;
    pv4.unknown4 = NULL;
    pv4.type = 4;
    pv4.propId = 0;
    pv4.dataPtr = &testUint;
    pv4.dataSize = 4;
    DbgLog("[TestProps] calling setProp type=4...\n");
    __try {
        setProp(subObj, &pv4);
        DbgLog("[TestProps] type=4 setProp OK\n");
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        DbgLog("[TestProps] type=4 setProp FAILED code=0x%08x\n", GetExceptionCode());
        return -7;
    }

    const wchar_t* testStr = L"TestPool";
    PropertyValue pv;
    pv.unknown0 = NULL;
    pv.unknown4 = NULL;
    pv.type = 2;
    pv.propId = 0;
    pv.dataPtr = (void*)testStr;
    pv.dataSize = (int)((wcslen(testStr) + 1) * sizeof(wchar_t));

    __try {
        setProp(subObj, &pv);
        return 0;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        return -4;
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID lpReserved) {
    return TRUE;
}
