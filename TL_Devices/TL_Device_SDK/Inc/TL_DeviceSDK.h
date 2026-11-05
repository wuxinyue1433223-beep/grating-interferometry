#pragma once

#include <stddef.h>
#include <limits>
#include <string>

#include "TL_DeviceSDK_def.h"

using namespace TL_DEVICE;

#ifndef _TL_DEVICE_SDK_H
#define _TL_DEVICE_SDK_H

#ifdef TL_DEVICESDK_EXPORTS
#define TL_DEVICE_API __declspec(dllexport)
#else
#define TL_DEVICE_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

	TL_DEVICE_API _ErrorStatus GetError(void *obj_handle, char info_buffer[], int info_buffer_size);

	// Returns the SDK string properties
	TL_DEVICE_API void GetSDKPropertyString(_SDKProperty propertySel, char info_buffer[], int *info_buffer_size);

#pragma region Plugin Manager and Service Manager
	// Opens the plugin manager and load the pluginS from the installation directory of the software specified in the Registry
	TL_DEVICE_API void OpenPluginManager();

	// Regists the device change notification event
	TL_DEVICE_API void RegisterDeviceChangeNotification(DeviceChangeNotification_EventHandler eventHandler, unsigned int *handlerID);

	// Unregists the device change notification event
	TL_DEVICE_API void UnregisterDeviceChangeNotification(unsigned int handlerID);

	// Gets the installation directory of the software
	TL_DEVICE_API void GetAppDirectory(char info_buffer[], int *info_buffer_size);

	// Gets the number of available plugin modules
	TL_DEVICE_API int NumAvailablePluginModules();

	// Gets the plugin module specified by the module index
	TL_DEVICE_API TL_DEVICE::ModuleHandle GetPluginModule(int moduleIndex);

	// Returns the plugin string properties
	TL_DEVICE_API void GetModuleProperty(TL_DEVICE::ModuleHandle hModule, TL_DEVICE::_ModuleProperty  propertySel, char info__buffer[], int *info_buffer_size);

	// Gets the number of plugin devices from the specifed module
	TL_DEVICE_API int NumDevices(TL_DEVICE::ModuleHandle hModule);

	// Gets the number of plugin devices from the specifed module
	TL_DEVICE_API TL_DEVICE::DeviceHandle GetPluginDevice(TL_DEVICE::ModuleHandle hModule, int deviceIndex);

	// Returns the device property
	TL_DEVICE_API void GetDeviceProperty(DeviceHandle hDev, TL_DEVICE::_DeviceProperty  propertySel, char info_buffer[], int *info_buffer_size);

	//// Closes the plugin manager
	//TL_DEVICE_API void ClosePluginManager();
#pragma endregion

#pragma region Device
	TL_DEVICE_API bool OpenDevice(TL_DEVICE::DeviceHandle hDev);
	TL_DEVICE_API bool CloseDevice(TL_DEVICE::DeviceHandle hDev);

	// Export configuration from device	
	TL_DEVICE_API void ExportConfigurationToFile(TL_DEVICE::DeviceHandle hDev, const char fileName[]);

	// API functions for Parameter operation
	TL_DEVICE_API TL_DEVICE::ParameterHandle GetParameterHandle(TL_DEVICE::DeviceHandle hDev, const char parameterName[]);
	TL_DEVICE_API void GetParameterProperty(TL_DEVICE::ParameterHandle hParam, char param_name[], char param_unit[], char data_type[], int *access_mode, int *data_size, int *num_channels);
	TL_DEVICE_API void GetParameterRange(TL_DEVICE::ParameterHandle hParam, _TLDeviceChannel channel_id, void *min, void *max, int size_of_variable);
	TL_DEVICE_API void GetParameterValueFromDevice(TL_DEVICE::DeviceHandle hDev, TL_DEVICE::ParameterHandle hParam, _TLDeviceChannel channel_id, void *value_out, int size_of_value_out);
	TL_DEVICE_API void SetParameterValueToDevice(TL_DEVICE::DeviceHandle hDev, TL_DEVICE::ParameterHandle hParam, _TLDeviceChannel channel_id, void *value_in, int size_of_value_in);

	// API functions for Array operation
	TL_DEVICE_API TL_DEVICE::ArrayHandle GetArrayHandle(TL_DEVICE::DeviceHandle hDev, const char arrayName[]);
	TL_DEVICE_API void GetArrayProperty(TL_DEVICE::ArrayHandle hArray, char array_name[], char array_unit[], char array_data_type[], int *access_mode, int *dimensions, int *size1, int *size2, int *size3, int *num_channels);
	TL_DEVICE_API void GetArrayDataFromDevice(TL_DEVICE::DeviceHandle hDev, TL_DEVICE::ArrayHandle hSrcArray, _TLDeviceChannel channel_id, void *pDstData);
	TL_DEVICE_API void SetArrayDataToDevice(TL_DEVICE::DeviceHandle hDev, TL_DEVICE::ArrayHandle hDstArray, _TLDeviceChannel channel_id, void *pSrcData);

	// API functions for Command operation
	TL_DEVICE_API CommandHandle GetCommandHandle(TL_DEVICE::DeviceHandle hDev, const char commandName[]);
	TL_DEVICE_API void GetCommandProperty(TL_DEVICE::CommandHandle hCmd, char command_name[], int *command_id, int *num_inputs, int *num_outputs);	
	TL_DEVICE_API void ExecuteCommand(TL_DEVICE::DeviceHandle hDev, TL_DEVICE::CommandHandle hCmd, _TLDeviceChannel channel_id);

	// API functions for COMM Port communication
	TL_DEVICE_API bool OpenComPort(TL_DEVICE::DeviceHandle hDev);
	TL_DEVICE_API void WriteComPort(TL_DEVICE::DeviceHandle hDev, const char txData[], char rxData[], int *rxData_size);
	TL_DEVICE_API void CloseComPort(TL_DEVICE::DeviceHandle hDev);

	// API functions for HID communication
	TL_DEVICE_API void SubscribeToHIDEvent(TL_DEVICE::DeviceHandle hDev, HIDReportRead_EventHandler eventHandler, unsigned int *handlerID);
	TL_DEVICE_API void UnsubscribeToHIDEvent(TL_DEVICE::DeviceHandle hDev, unsigned int handlerID);
	TL_DEVICE_API bool IsHIDEventUnsubscribed(TL_DEVICE::DeviceHandle hDev);
#pragma endregion

//	// Test function for Python wrapper
	TL_DEVICE_API int GetValue_int(int input);
	TL_DEVICE_API double GetValue_double(double input);
	TL_DEVICE_API double GetValue_doublePtr(double *input);	

#ifdef __cplusplus
}
#endif

#endif // _TL_DEVICE_SDK_H