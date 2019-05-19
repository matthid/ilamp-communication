/**
 *  Horevo LED Lamp
 *
 *  Copyright 2019 Matthias Dittrich
 *
 *  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except
 *  in compliance with the License. You may obtain a copy of the License at:
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 *  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed
 *  on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License
 *  for the specific language governing permissions and limitations under the License.
 *
 */
 /**

DeviceHandler example:

https://gist.github.com/joshua-moore/7c8f8355f217770bae56
 */
metadata {
	definition (name: "Horevo LED Lamp", executeCommandsLocally: true, namespace: "matthid", author: "Matthias Dittrich") {
		capability "Bulb"
		capability "Color Control"
		capability "Color Temperature"
		capability "Light"
		capability "Switch"
		capability "Switch Level"
	}

	preferences {
        input("baseUriHost", "string",
            title: "Base Address Hostname",
            description: "The host name of the Horevo Lamp Server",
            required: true,
            displayDuringSetup: true
        )
        input("baseUriPort", "string",
            title: "Base Address Port",
            description: "The port of the Horevo Lamp Server",
            required: true,
            displayDuringSetup: true
        )
        input("macAddress", "string",
            title: "MAC Address",
            description: "MAC Address of the Horevo Lamp",
            required: true,
            displayDuringSetup: true
        )
    }

	simulator {
		// TODO: define status and reply messages here
	}

	tiles(scale: 2) {
        multiAttributeTile(name:"switch", type: "lighting", width: 6, height: 4, canChangeIcon: true){
            tileAttribute ("device.switch", key: "PRIMARY_CONTROL") {
                attributeState "on", label:'${name}', action:"switch.off", icon:"st.lights.philips.hue-single", backgroundColor:"#79b821", nextState:"off"
                attributeState "off", label:'${name}', action:"switch.on", icon:"st.lights.philips.hue-single", backgroundColor:"#ffffff", nextState:"on"
            }
            tileAttribute ("device.level", key: "SLIDER_CONTROL") {
                attributeState "level", action:"switch level.setLevel"
            }
            tileAttribute ("device.color", key: "COLOR_CONTROL") {
                attributeState "color", action:"color control.setColor"
            }
        }
        controlTile("colorTempSliderControl", "device.colorTemperature", "slider", width: 4, height: 2, range:"(0..100)") {
            state "colorTemperature", action:"color temperature.setColorTemperature"
        }
        valueTile("colorTemp", "device.colorTemperature", decoration: "flat", width: 2, height: 2) {
            state "colorTemperature", label: '${currentValue} K',
				backgroundColors:[
					[value: 2900, color: "#FFA757"],
					[value: 3300, color: "#FFB371"],
					[value: 3700, color: "#FFC392"],
					[value: 4100, color: "#FFCEA6"],
					[value: 4500, color: "#FFD7B7"],
					[value: 4900, color: "#FFE0C7"],
					[value: 5300, color: "#FFE8D5"],
                    [value: 6600, color: "#FFEFE1"]
				]
        }
        standardTile("refresh", "device.switch", decoration: "flat", width: 2, height: 2) {
            state "default", label:"", action:"refresh.refresh", icon:"st.secondary.refresh"
        }

        main(["switch"])
        details(["switch", "colorTempSliderControl", "colorTemp", "refresh"])    
	}
}

// parse events into attributes
def parse(String description) {
	log.debug "Parsing '${description}'"
	// TODO: handle 'switch' attribute
	// TODO: handle 'hue' attribute
	// TODO: handle 'saturation' attribute
	// TODO: handle 'color' attribute
	// TODO: handle 'colorTemperature' attribute
	// TODO: handle 'switch' attribute
	// TODO: handle 'switch' attribute
	// TODO: handle 'level' attribute

}

// Store the MAC address as the device ID so that it can talk to SmartThings
def setNetworkAddress() {
    // Setting Network Device Id
    /*
    def hex = "$settings.mac".toUpperCase().replaceAll(':', '')
    if (device.deviceNetworkId != "$hex") {
        device.deviceNetworkId = "$hex"
        log.debug "Device Network Id set to ${device.deviceNetworkId}"
    }*/
}

// Store the MAC address as the device ID so that it can talk to SmartThings
def sendAction(action) {
    if (device.hub == null) {
        log.error "Hub is null, must set the hub in the device settings so we can get local hub IP and port"
        return
    }
    
    setNetworkAddress()
    
    def headers = [:]
    headers.put("HOST", "$baseUriHost:$baseUriPort")
    headers.put("Content-Type", "application/json")

	log.debug "Sending action '/api/lamps/${macAddress}?action=${action}'"
    def hubAction = new physicalgraph.device.HubAction(
        method: "GET",
        path: "/api/lamps/${macAddress}?action=${action}",
        headers: headers
    )
    sendHubCommand(hubAction)
    
    /*
    def params = [
        uri: baseUri,
        path: "/api/lamps/${macAddress}?action=${action}"
    ]
    try {
        httpGet(params) { resp ->
            resp.headers.each {
                log.debug "${it.name} : ${it.value}"
            }
        
            log.debug "response contentType: ${resp.contentType}"
            log.debug "response data: ${resp.data}"
        }
    } catch (e) {
        log.error "something went wrong: $e"
    }*/
}

// handle commands
def off() {
	log.debug "Executing 'off'"
    sendAction("poweroff")
    state.mode = "off"
    sendEvent(name: "switch", value: "off")
}

def installed () {
	state.mode = "off"
    state.lastMode = "white"
    state.level = 0.5
    state.temperature = 0.2
    state.color = "FF0000"
}

def on() {
	log.debug "Executing 'on'"
    def level = state.level == 0 ? 0.5 : state.level
    state.level = level
    if (state.lastMode == "white") {
    	state.mode = "white"
    	sendAction("poweron_white")
        action("set_warm_brightness&brightness=${level}")
    } else if (state.lastMode == "color") {
    	state.mode = "color"
    	sendAction("poweron_color")
    } else {
    	installed()
    	state.mode = "white"
    	sendAction("poweron_white")
        action("set_warm_brightness&brightness=${level}")
    }
    
    sendEvent(name: "switch", value: "on", isStateChange: true)
}

def setHue(value) {
	log.debug "Executing 'setHue(${value})'"
	// TODO: handle 'setHue' command
}

def setSaturation(value) {
	log.debug "Executing 'setSaturation(${value})'"
	// TODO: handle 'setSaturation' command
}

def setColor(value) {
	log.debug "Executing 'setColor(${value})'"
    // stupid hack to set the state the first time this device is used. Won't select a color other than #FFFFFF without it.
	//if (!value?.color) state.color = "#FF0000"
    def color_action = value.hex - "#"
    state.color = color_action
	log.debug "setColor, level: '${state.level}', mode: '${state.mode}', color: '${color_action}'"
    if (state.level != 0) {
        if (state.mode != "color") {
    		sendAction("poweron_color")
        }
    	sendAction("set_color&brightness=${state.level}&color=${color_action}")
    }
    
    state.mode = "color"
    state.lastMode = "color"
    
    sendEvent(name: "color", value: value.hex, data: value)
}

def setColorTemperature(value) {
	log.debug "Executing 'setColorTemperature(${value})'"
    if (state.mode != "white") {
        sendAction("poweron_white")
        state.mode = "white"
        state.lastMode = "white"
    }
    
    def temp_percentage = value == 0 ? 0 : value / 100.0;
    state.temperature = temp_percentage
    sendAction("set_warm_color&color=${temp_percentage}")
    sendEvent(name: "colorTemperature", value: value, isStateChange: true)
}

def setLevel(value) {
	log.debug "Executing 'setLevel(${value})'"
    if (value == 0) {
        sendAction("poweroff")
        state.level = 0
        state.mode = "off"
        sendEvent(name: "switch", value: "off")
    } else {
    	def temp_percentage = value / 100.0
        state.level = temp_percentage
        def isOffline = state.mode == "off"
        if (state.lastMode == "color") {
        	state.mode = "color"
        	if (isOffline) {
            	sendAction("poweron_color")
    			sendEvent(name: "switch", value: "on")
            }
    		sendAction("set_color&brightness=${temp_percentage}&color=${state.color}")
        } else {
        	state.mode = "white"
        	if (isOffline) {
            	sendAction("poweron_white")
    			sendEvent(name: "switch", value: "on")
            }
        	sendAction("set_warm_brightness&brightness=${temp_percentage}")
        }
    }
    
	sendEvent(name: "level", value: value, isStateChange: true)
}