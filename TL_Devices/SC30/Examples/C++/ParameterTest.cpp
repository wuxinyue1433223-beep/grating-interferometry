#include "TestFunctions.h"

int ParameterTest()
{
	std::cout << std::endl;
	std::cout << "This example is to demonstrate how to get and set parameter and data from/to the SC30." << std::endl;
	std::cout << std::endl;

	// Set SC30 in Internal mode
	ParameterHandle hMode = GetParameterHandle(hSC30_Device, "active_mode");
	byte mode = 2;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hMode, _TLDeviceChannel::NO_CHANNEL, &mode, sizeof(byte)), hSC30_Device);
	wait(30);

	// Set Signal type to Frequency/DutyCycle for Channel 1
	ParameterHandle hInt_SignalType = GetParameterHandle(hSC30_Device, "internal_mode_signal_type");
	byte signalType = 0;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hInt_SignalType, _TLDeviceChannel::CHANNEL1, &signalType, sizeof(byte)), hSC30_Device);
	wait(30);
	
	// Set Open time to 500.0 ms (or 100.0 ms) for Channel 1
	ParameterHandle hInt_Open_Time = GetParameterHandle(hSC30_Device, "internal_mode_open_time");
	
	float internal_mode_open_time = 500.0f;
	SDKErrChk(GetParameterValueFromDevice(hSC30_Device, hInt_Open_Time, _TLDeviceChannel::CHANNEL1, &internal_mode_open_time, sizeof(float)), hSC30_Device);
	std::cout << "Channel 1 interanl mode open time is: " << internal_mode_open_time << std::endl;
	wait(30);
	internal_mode_open_time = (internal_mode_open_time == 100.0f) ? 500.0f : 100.0f;
	std::cout << "Set Channel 1 internal mode open time to " << internal_mode_open_time << std::endl;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hInt_Open_Time, _TLDeviceChannel::CHANNEL1, &internal_mode_open_time, sizeof(float)), hSC30_Device);
	wait(30);

	// Set Open time unit to ms for Channel 1
	ParameterHandle hInt_Open_Time_UOM = GetParameterHandle(hSC30_Device, "internal_mode_open_time_unit");
	
	byte open_time_uom = 0;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hInt_Open_Time_UOM, _TLDeviceChannel::CHANNEL1, &open_time_uom, sizeof(byte)), hSC30_Device);

	// Set Close time to 100.0 (or 500.0) ms for Channel 1
	ParameterHandle hInt_Close_Time = GetParameterHandle(hSC30_Device, "internal_mode_close_time");	

	float internal_mode_close_time = 0.0f;
	SDKErrChk(GetParameterValueFromDevice(hSC30_Device, hInt_Close_Time, _TLDeviceChannel::CHANNEL1, &internal_mode_close_time, sizeof(float)), hSC30_Device);
	std::cout << "Channel 1 interanl mode close time is: " << internal_mode_close_time << std::endl;
	wait(30);
	internal_mode_close_time = (internal_mode_open_time == 100.0f) ? 500.0f : 100.0f;
	std::cout << "Set Channel 1 internal mode close time to " << internal_mode_close_time << std::endl;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hInt_Close_Time, _TLDeviceChannel::CHANNEL1, &internal_mode_close_time, sizeof(float)), hSC30_Device);
	wait(30);

	// Set Close time unit to ms for Channel 1
	ParameterHandle hInt_Mode_Close_Time_UOM = GetParameterHandle(hSC30_Device, "internal_mode_close_time_unit");
	byte close_time_uom = 0;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hInt_Mode_Close_Time_UOM, _TLDeviceChannel::CHANNEL1, &close_time_uom, sizeof(byte)), hSC30_Device);
	wait(30);

	internal_mode_open_time = 0;
	SDKErrChk(GetParameterValueFromDevice(hSC30_Device, hInt_Open_Time, _TLDeviceChannel::CHANNEL1, &internal_mode_open_time, sizeof(float)), hSC30_Device);	
	std::cout << "Channel 1 interanl mode open time is: " << internal_mode_open_time << std::endl;
	wait(30);
	
	internal_mode_close_time = 0;
	SDKErrChk(GetParameterValueFromDevice(hSC30_Device, hInt_Close_Time, _TLDeviceChannel::CHANNEL1, &internal_mode_close_time, sizeof(float)), hSC30_Device);
	std::cout << "Channel 1 internal mode close time is: " << internal_mode_close_time << std::endl;
	wait(30);
	
	// Set signal type to Frequency/DutyCycle for Channel 2
	signalType = 1;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hInt_SignalType, _TLDeviceChannel::CHANNEL2, &signalType, sizeof(byte)), hSC30_Device);
	wait(30);
	
	// Set internal mode frequency for Channe2 1 to 1.0Hz (or 3.0Hz)
	ParameterHandle hInt_Freq = GetParameterHandle(hSC30_Device, "internal_mode_frequency");

	float internal_mode_frequency = 0.0f;
	SDKErrChk(GetParameterValueFromDevice(hSC30_Device, hInt_Freq, _TLDeviceChannel::CHANNEL2, &internal_mode_frequency, sizeof(float)), hSC30_Device);
	std::cout << "Channel 2 internal mode frequency is: " << internal_mode_frequency << std::endl;
	internal_mode_frequency = (internal_mode_frequency == 1.0) ? 3.0 : 1.0;
	std::cout << "Set Channel 2 internal mode frequency to " << internal_mode_frequency << std::endl;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hInt_Freq, _TLDeviceChannel::CHANNEL2, &internal_mode_frequency, sizeof(float)), hSC30_Device);
	wait(30);

	// Set internal mode duty cycle for Channel 2 to 40.0% (or 60.0%)
	ParameterHandle hInt_DutyCycle = GetParameterHandle(hSC30_Device, "internal_mode_dutycycle");

	float internal_mode_dutycycle = 0.0f;
	SDKErrChk(GetParameterValueFromDevice(hSC30_Device, hInt_DutyCycle, _TLDeviceChannel::CHANNEL2, &internal_mode_dutycycle, sizeof(float)), hSC30_Device);
	std::cout << "Channel 2 internal mode duty cycle is: " << internal_mode_dutycycle << std::endl;
	wait(30);	
	internal_mode_dutycycle = (internal_mode_dutycycle == 40.0f) ? 60.0f : 40.0f;
	std::cout << "Set Channel 2 internal mode duty cycle to " << internal_mode_dutycycle << std::endl;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hInt_DutyCycle, _TLDeviceChannel::CHANNEL2, &internal_mode_dutycycle, sizeof(float)), hSC30_Device);
	wait(30);

	internal_mode_frequency = 0.0f;
	SDKErrChk(GetParameterValueFromDevice(hSC30_Device, hInt_Freq, _TLDeviceChannel::CHANNEL2, &internal_mode_frequency, sizeof(float)), hSC30_Device);
	std::cout << "Channel 2 interanl mode frequency is: " << internal_mode_frequency << std::endl;
	wait(30); 
	
	internal_mode_dutycycle = 0.0f;
	SDKErrChk(GetParameterValueFromDevice(hSC30_Device, hInt_DutyCycle, _TLDeviceChannel::CHANNEL2, &internal_mode_dutycycle, sizeof(float)), hSC30_Device);
	std::cout << "Channel 2 internal mode duty cycle is: " << internal_mode_dutycycle << std::endl;
	wait(30);

	return Rtn_OK;
}