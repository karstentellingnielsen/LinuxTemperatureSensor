# Introduction
This is a small side project to get a temperature reading from a linux edge device as an IoT edge module.

The module will be applicable for all linux IoT edge devices. It will not work in a windows device. For that another solution is needed.

# Status
The module is able to report core temperatures as reported by lm-sensors. The data is recieved in the IoT hub.
Things to do:
* Logging configuration do not work - all levels are logged
* Controlling poll frequency as an message command is not tested
* Create configuration documentation
 
But the base module is up and running.

# Configuration and usage

The solution reports reading with a specified period. The default period is 900 seconds. 
The behaviour is configured by a standard configuration file: 
```
{
  "Period":  "900",
  "Logging": {
    "EventLog": {
      "LogLevel": {
        "Default": "Information",
        "Microsoft": "Warning",
        "System": "Warning",
      }
    }
  }
}
```
The parameters can be overwritten by setting environment variables. 

Currently updating the reporting period by sending a command is not tested. 



## Temperature reporting messages
The module reports temperature as recived directly from the `sensors -j` command. 
No formatting takes place. As a result the messsage generated will depend on the BIOS and the Linux installation.
Below is an example message as reporten UNO-127 and Ubuntu 22 kernel:
```
{
  "acpitz-acpi-0": {
    "Adapter": "ACPI interface",
    "temp1": {
      "temp1_input": -1,
      "temp1_crit": 255
    },
    "temp2": {
      "temp2_input": 0,
      "temp2_crit": 119
    }
  },
  "coretemp-isa-0000": {
    "Adapter": "ISA adapter",
    "Package id 0": {
      "temp1_input": 43,
      "temp1_max": 105,
      "temp1_crit": 105,
      "temp1_crit_alarm": 0
    },
    "Core 0": {
      "temp2_input": 36,
      "temp2_max": 105,
      "temp2_crit": 105,
      "temp2_crit_alarm": 0
    },
    "Core 1": {
      "temp3_input": 36,
      "temp3_max": 105,
      "temp3_crit": 105,
      "temp3_crit_alarm": 0
    },
    "Core 2": {
      "temp4_input": 36,
      "temp4_max": 105,
      "temp4_crit": 105,
      "temp4_crit_alarm": 0
    },
    "Core 3": {
      "temp5_input": 37,
      "temp5_max": 105,
      "temp5_crit": 105,
      "temp5_crit_alarm": 0
    }
  }
}
```

# Design
The module uses the linux lm-sensor package to get the temperature. The resulting json (```sensors -j```) is shipped as a package to the Hub without any modification.

Installation of the package can be found here: https://www.cyberciti.biz/faq/how-to-check-cpu-temperature-on-ubuntu-linux/


The rest of the project is cloned from the Microsoft temperature simulation project:
https://github.com/Azure/iotedge/tree/main/edge-modules/SimulatedTemperatureSensor

The simulation and data structures are removed, and calls to the sensors program is used instead.


# Links:
Some usefull links. This is mostly a scratch area for now. I will do contnous cleanup as a go allong.



https://azuremarketplace.microsoft.com/en/marketplace/apps/azure-iot.simulated-temperature-sensor?tab=Overview

## Logging and monitoring solution
https://github.com/Azure-Samples/iotedge-logging-and-monitoring-solution/blob/main/README.md