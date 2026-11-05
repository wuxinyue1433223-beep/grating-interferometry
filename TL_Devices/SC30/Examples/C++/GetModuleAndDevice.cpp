#include "TestFunctions.h"

int GetModuleAndDevice()
{
	// Get modules
	numModules = 0;
	if (findPluginModules() == Rtn_ERROR)
	{
		std::cout << std::endl;
		std::cout << "Error occurred when finding the available Thorlabs device modules." << std::endl;
		return Rtn_ERROR;
	}

	if (numModules > 0)
	{
		for (int moduleIndex = 1; moduleIndex <= numModules; moduleIndex++)
		{
			ModuleHandle hModule = GetPluginModule(moduleIndex);

			std::cout << std::endl;

			strLength = STRING_LENGTH_512;
			SDKErrChk(GetModuleProperty(hModule, _ModuleProperty::Module_Name, str, &strLength), hModule);
			std::cout << "Module Name: " << str << std::endl;

			strLength = STRING_LENGTH_512;
			SDKErrChk(GetModuleProperty(hModule, _ModuleProperty::Module_Path, str, &strLength), hModule);
			std::cout << "Module Path: " << str << std::endl;

			strLength = STRING_LENGTH_512;
			SDKErrChk(GetModuleProperty(hModule, _ModuleProperty::Module_MajorVersion, str, &strLength), hModule);

			strLength = STRING_LENGTH_512;
			SDKErrChk(GetModuleProperty(hModule, _ModuleProperty::Module_MinorVersion, str1, &strLength), hModule);
			std::cout << "Module Version: " << str << "." << str1 << std::endl;

			strLength = STRING_LENGTH_512;
			SDKErrChk(GetModuleProperty(hModule, _ModuleProperty::Module_ProductID, str, &strLength), hModule);
			std::cout << "Module Product ID: " << str << std::endl;

			unsigned short productID = std::stoi(str, 0, 16);

			strLength = STRING_LENGTH_512;
			SDKErrChk(GetModuleProperty(hModule, _ModuleProperty::Module_VendorID, str, &strLength), hModule);
			std::cout << "Module Vendor ID: " << str << std::endl;

			unsigned short vendorID = std::stoi(str, 0, 16);

			// ProductID of SC30 is 0x100E, VendorID is 0x1313
			if (productID == 4110 && vendorID == 4883)
			{
				hSC30_Module = hModule;

				numDevices = 0;
				if (findDeviceHandles(hModule) == Rtn_ERROR)
				{
					std::cout << std::endl;
					std::cout << "Error occurred when trying to find the connected devices of the specified Thorlabs device module." << std::endl;
					return Rtn_ERROR;
				}

				if (numDevices > 0)
				{
					for (int deviceIndex = 1; deviceIndex <= numDevices; deviceIndex++)
					{
						TL_DEVICE::DeviceHandle hDev = GetPluginDevice(hSC30_Module, deviceIndex);

						// Get the device handle of first found SC30 and use it in the rest of the examples
						if (hSC30_Module != NULL &&
							hSC30_Device == NULL)
						{
							hSC30_Device = hDev;
						}

						std::cout << std::endl;

						strLength = STRING_LENGTH_512;
						SDKErrChk(GetDeviceProperty(hDev, _DeviceProperty::Device_Name, str, &strLength), hDev);
						std::cout << "Device Name: " << str << std::endl;

						strLength = STRING_LENGTH_512;
						SDKErrChk(GetDeviceProperty(hDev, _DeviceProperty::Device_SN, str, &strLength), hDev);
						std::cout << "Device Serial Number: " << str << std::endl;

						_sc30_deviceMap[std::string(str)] = hDev;
					}
				}
			}
		}
	}

	return Rtn_OK;
}