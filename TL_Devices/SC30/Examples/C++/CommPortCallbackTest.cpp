#include "TestFunctions.h"

int CommPortCallbackTest()
{
	ParameterHandle hPollingInterval = GetParameterHandle(hSC30_Device, "polling_interval");;
	unsigned short polling_interval = 50;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hPollingInterval, _TLDeviceChannel::CHANNEL2, &polling_interval, sizeof(unsigned short)), hSC30_Device);
	wait(30);

	unsigned int eventHandlerID;
	SubscribeToCommPortEvent(hSC30_Device, CommPortCallbackHandler, &eventHandlerID);
	ErrChk("SubscribeToCommPortEvent", hSC30_Device);
	
	std::cout << "Press any key to start polling..." << std::endl;
	getchar();

	ParameterHandle hStartPolling = GetParameterHandle(hSC30_Device, "start_polling");;
	unsigned char startPolling = 1;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hStartPolling, _TLDeviceChannel::CHANNEL2, &startPolling, sizeof(unsigned char)), hSC30_Device);
	wait(30);

	getchar();

	UnsubscribeToCommPortEvent(hSC30_Device, eventHandlerID);
	ErrChk("UnsubscribeToCommPortEvent", hSC30_Device);

	bool isCommPortEventUnsbucribed = false;
	auto start_time = std::chrono::high_resolution_clock::now();

	int milSeconds = 0;
	while (!isCommPortEventUnsbucribed && milSeconds < 3000)
	{
		isCommPortEventUnsbucribed = IsCommPortEventUnsubscribed(hSC30_Device);
		auto end_time = std::chrono::high_resolution_clock::now();
		auto time = end_time - start_time;
		milSeconds = time / std::chrono::milliseconds(1);
	}

	return Rtn_OK;
}