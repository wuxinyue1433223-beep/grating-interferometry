#pragma once

#include <windows.h>
#include <stdio.h>
#include <conio.h>
#include <shlobj.h>
#include <string>
#include <iostream>
#include <fstream>
#include <iomanip>
#include <thread>
#include <chrono>
#include <map>
#include <vector>

#include "TL_DeviceSDK.h"

#define Rtn_OK						0
#define Rtn_ERROR					-1

// For functions without return value
#define SDKErrChk(functionCall, obj_handle) functionCall; if (GetError(obj_handle, errStr, STRING_LENGTH_512) != TL_DEVICE::_ErrorStatus::NoError) {std::cout << "\nError: " << errStr << "\n\nPress any key to exit the testing.\n"; getchar(); return Rtn_ERROR-1;}

// For functions with return value
#define ErrChk(functionName, obj_handle) if (GetError(obj_handle, errStr, STRING_LENGTH_512) != TL_DEVICE::_ErrorStatus::NoError) {std::cout << "\nError: " << errStr << "\n\nPress any key to exit the testing.\n"; getchar(); return Rtn_ERROR;}

typedef std::map<std::string, void *> DeviceMap;

extern DeviceMap _sc30_deviceMap;

extern char errStr[STRING_LENGTH_512];

extern char str[STRING_LENGTH_512];
extern char str1[STRING_LENGTH_512];
extern int strLength;
extern int strLength1;

extern char c_szPath[STRING_LENGTH_512];

extern int numModules;

extern ModuleHandle hSC30_Module;

extern DeviceHandle *deviceHandles;
extern int numDevices;

extern DeviceHandle hSC30_Device;

extern ArrayHandle hFriendlyName;
extern int friendlyNameArraySize1;

extern bool isDeviceOpen;
extern bool isThreadTerminated;

extern int reportSize;

void wait(int delay_in_ms);

int GetModuleAndDevice();
int findPluginModules();
int findDeviceHandles(ModuleHandle moduleHandle);

//int GetVarAndHyperTerminalCmdList();
//int getVarList();
//int getHyperTerminalCmdList();

int SystemNotificationTest();
void deviceChangeNotificationHandler(_NotificationParam *notification_param);

//int ParameterAndDataTest();
//
//int HyperTerminalTest();

int CommPortCallbackTest();
void CommPortCallbackHandler(const char* status);

int ArrayTest();

int ParameterTest();

int ProfileTest();

int HyperTerminalTest();

int ExportConfiguration();