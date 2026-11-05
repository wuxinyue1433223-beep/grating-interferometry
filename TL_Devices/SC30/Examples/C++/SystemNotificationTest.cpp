#include "TestFunctions.h"

int SystemNotificationTest()
{
	std::cout << std::endl;
	std::cout << "This example is to demonstrate how to receive the Device_Arrival and Device_Removal notificaiton for Thorlabs deivces." << std::endl;

	if (isDeviceOpen)
	{
		SDKErrChk(CloseDevice(hSC30_Device), hSC30_Device);
		isDeviceOpen = false;
	}

	unsigned int eventHandlerID;
	SDKErrChk(RegisterDeviceChangeNotification(deviceChangeNotificationHandler, &eventHandlerID), NULL);

	if (_sc30_deviceMap.size() > 0)
	{
		std::cout << "\nRemove the SC30 from the USB connection, and wait the notification...";
		getchar();

		std::cout << "\nConnect the SC30, and wait the notification...";
		getchar();
	}
	else
	{
		std::cout << "\nConnect the SC30, and wait the notification...";
		getchar();

		std::cout << "\nRemove the SC30 from the USB connection, and wait the notification...";
		getchar();
	}

	SDKErrChk(UnregisterDeviceChangeNotification(eventHandlerID), NULL);

	return Rtn_OK;
}