#pragma once

#include <map>

#define MAJOR_VERSION 1
#define MINOR_VERSION 0

#define STRING_LENGTH_16		16
#define STRING_LENGTH_32		32
#define STRING_LENGTH_64		64
#define STRING_LENGTH_128		128
#define STRING_LENGTH_256		256
#define STRING_LENGTH_512		512
#define STRING_LENGTH_8192		8192
#define MAX_COMMPORT_BUFFER		32768

#define TRUE 1
#define FALSE 0

typedef int BOOL;

namespace TL_DEVICE
{
	// Plugin Manager Handle
	struct C_PluginManager;
	typedef struct C_PluginManager *PluginManagerHandle;

	// Module Handle
	struct C_ModuleHandle;
	typedef struct C_ModuleHandle *ModuleHandle;

	// Plugin Handle
	struct C_DeviceHandle;
	typedef struct C_DeviceHandle *DeviceHandle;

	// Parameter Handle
	struct C_Parameter;
	typedef struct C_Parameter *ParameterHandle;

	// Array Handle
	struct C_Array;
	typedef struct C_Array *ArrayHandle;

	// Command Handle
	struct C_Command;
	typedef struct C_Command *CommandHandle;

	enum _SDKProperty
	{
		SDK_MajorVersion,
		SDK_MinorVersion
	};

	enum _DeviceChangeNotification
	{
		Device_Arrival,
		Device_Removal,
	};

	struct _NotificationParam
	{
		_DeviceChangeNotification _device_change_notification;
		void *_module;
		void *_device;
	};

	enum _ModuleProperty
	{
		Module_Name,
		Module_Path,
		Module_VendorID,
		Module_ProductID,
		Module_MajorVersion,
		Module_MinorVersion,
	};

	enum _DeviceProperty
	{
		Device_VendorID,
		Device_ProductID,
		Device_HID_DevicePath,
		Device_CommPort_DevicePath,
		Device_CommPortName,
		Device_SN,
		Device_Name,
		Device_Manufacturer,
		Device_Version,
		Device_DisplayVersion,
		Device_DriverPath,
		Device_Configuration_Version
	};

	enum _ErrorStatus
	{
		NoError = 0,
		Error = 1,
		Exception = 2,
		FatalError = 3
	};

	enum _DataType
	{
		NotApplicable = -1,
		UChar,
		Char,
		UShort,
		Short,
		UInt32,
		Int32,
		Float,
		Double,
		String,
	};

	enum _DataAccessMode
	{
		read = 1,
		write,
		read_write,
	};

	enum _DeviceStatus
	{
		DeviceNotOperable = 0,				// 0x0000 - 0000 0000 0000 0000
		Ready = 1,							// 0x###1 - 0000 0000 0000 0001
		DeviceOpen = 2,						// 0x###2 - 0000 0000 0000 0010
		InitializationRequired = 4,			// 0x###4 - 0000 0000 0000 0100
		ExceptionOrError = 8,				// 0x###8 - 0000 0000 0000 1000	
	};

	enum _TLDeviceChannel
	{
		NO_CHANNEL = 0,
		CHANNEL1,
		CHANNEL2,
		CHANNEL3,
		CHANNEL4,
		CHANNEL5,
		CHANNEL6,
		CHANNEL7,
		CHANNEL8
	};

	enum _CommandIOType
	{
		input = 0,
		output,
	};

	typedef void(*DeviceChangeNotification_EventHandler)(_NotificationParam *notification_param);
	typedef void(*HIDReportRead_EventHandler)(unsigned int hid_report_id);
}