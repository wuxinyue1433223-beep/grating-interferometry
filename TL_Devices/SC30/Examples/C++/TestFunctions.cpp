#include "TestFunctions.h"
void wait(int delay_in_ms)
{
	auto start_time = std::chrono::high_resolution_clock::now();

	int milSeconds = 0;
	while (milSeconds < delay_in_ms)
	{
		auto end_time = std::chrono::high_resolution_clock::now();
		auto time = end_time - start_time;
		milSeconds = time / std::chrono::milliseconds(1);
	}
}

int findPluginModules()
{
	numModules = NumAvailablePluginModules();

	ErrChk("NumAvailablePluginModules", NULL);

	return Rtn_OK;
}

int findDeviceHandles(ModuleHandle moduleHandle)
{
	numDevices = NumDevices(moduleHandle);
	ErrChk("NumDevices", moduleHandle);

	return Rtn_OK;
}

void deviceChangeNotificationHandler(_NotificationParam *notification_param)
{
	if (notification_param->_device_change_notification == _DeviceChangeNotification::Device_Arrival)
	{
		std::cout << std::endl;
		std::cout << std::endl;

		strLength = STRING_LENGTH_512;
		GetModuleProperty((ModuleHandle)notification_param->_module, _ModuleProperty::Module_ProductID, str, &strLength);
		std::cout << "Module Product ID: " << str << std::endl;

		unsigned short productID = std::stoi(str, 0, 16);

		strLength = STRING_LENGTH_512;
		GetModuleProperty((ModuleHandle)notification_param->_module, _ModuleProperty::Module_VendorID, str, &strLength);
		std::cout << "Module Vendor ID: " << str << std::endl;

		unsigned short vendorID = std::stoi(str, 0, 16);

		// ProductID of SC30 is 0x100C, VendorID is 0x1313
		if (productID == 4108 && vendorID == 4883)
		{
			hSC30_Module = (ModuleHandle)notification_param->_module;

			numDevices = NumDevices((ModuleHandle)notification_param->_module);

			if (numDevices > 0)
			{
				std::map<std::string, void *>::iterator it;

				for (int deviceIndex = 1; deviceIndex <= numDevices; deviceIndex++)
				{
					TL_DEVICE::DeviceHandle hDev = GetPluginDevice(hSC30_Module, deviceIndex);

					strLength = STRING_LENGTH_512;
					GetDeviceProperty(hDev, _DeviceProperty::Device_SN, str, &strLength);

					it = _sc30_deviceMap.find(std::string(str));
					if (it != _sc30_deviceMap.end())
					{
						hSC30_Device = hDev;

						std::cout << std::endl;
						std::cout << "\nDevice arrived." << std::endl;

						std::cout << "Device Serial Number: " << str << std::endl;

						strLength = STRING_LENGTH_512;
						GetDeviceProperty(hSC30_Device, _DeviceProperty::Device_Name, str, &strLength);
						std::cout << "Device Name: " << str << std::endl;
					}
				}
			}
		}

		std::cout << "\nPress any key to continue..." << std::endl;
	}
	else if (notification_param->_device_change_notification == _DeviceChangeNotification::Device_Removal)
	{
		strLength = STRING_LENGTH_512;
		GetModuleProperty((ModuleHandle)notification_param->_module, _ModuleProperty::Module_ProductID, str, &strLength);
		unsigned short productID = std::stoi(str, 0, 16);

		strLength = STRING_LENGTH_512;
		GetModuleProperty((ModuleHandle)notification_param->_module, _ModuleProperty::Module_VendorID, str, &strLength);
		unsigned short vendorID = std::stoi(str, 0, 16);

		// ProductID of Sc30 is 0x100C, VendorID is 0x1313
		if (productID == 4108 && vendorID == 4883)
		{
			numDevices = NumDevices((ModuleHandle)notification_param->_module);

			if (numDevices > 0)
			{
				hSC30_Module = (ModuleHandle)notification_param->_module;

				std::vector<std::string> _sc30_SerialNumber;
				for (int deviceIndex = 1; deviceIndex <= numDevices; deviceIndex++)
				{
					strLength = STRING_LENGTH_512;
					GetDeviceProperty(hSC30_Device, _DeviceProperty::Device_SN, str, &strLength);
					_sc30_SerialNumber.push_back(std::string(str));
				}

				std::string sn = "";
				std::map<std::string, void *>::iterator map_it;
				std::vector<std::string>::iterator vector_it;

				for (map_it = _sc30_deviceMap.begin(); map_it != _sc30_deviceMap.end(); ++map_it)
				{
					vector_it = find(_sc30_SerialNumber.begin(), _sc30_SerialNumber.end(), map_it->first);
					if (vector_it != _sc30_SerialNumber.end())
					{
						std::cout << std::endl;
						std::cout << std::endl;

						sn = map_it->first;

						std::cout << "SC30 with serial number " << str << " is removed." << std::endl;
					}
				}

				if (sn != "")
					_sc30_deviceMap.erase(sn);
			}
			else
			{
				if (_sc30_deviceMap.size() > 0)
				{
					std::cout << std::endl;
					std::cout << std::endl;

					std::cout << "SC30 with serial number " << _sc30_deviceMap.begin()->first << " is removed." << std::endl;
				}
			}
		}

		deviceHandles = NULL;

		std::cout << "\nPress any key to continue..." << std::endl;
	}
}

void CommPortCallbackHandler(const char* status)
{
	std::cout << "Comm Port status callback. Press any key to quit test." << std::endl;
}