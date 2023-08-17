# Introduction
This is a small side project to get a temperature reading from a linux edge device as an IoT edge module.

The module will be applicable for all linux IoT edge devices. It will not work in a windows device. For that another solution is needed.

# Design
The module uses the linux lm-sensor package to get the temperature. The resulting json (```sensors -j```) is shipped as a package to the Hub without any modification.

Installation of the package can be found here: https://www.cyberciti.biz/faq/how-to-check-cpu-temperature-on-ubuntu-linux/


The rest of the project is cloned from the Microsoft temperature simulation project:
https://github.com/Azure/iotedge/tree/main/edge-modules/SimulatedTemperatureSensor

The simulation and data structures are removed, and calls to the sensors program is used instead.


# Links:




https://azuremarketplace.microsoft.com/en/marketplace/apps/azure-iot.simulated-temperature-sensor?tab=Overview
