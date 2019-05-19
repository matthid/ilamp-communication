# ilamp-communication

This is a server for integration into Smartthings, based on https://github.com/samsam2310/Bluetooth-Chsmartbulb-Python-API

## How it works

Lamp <---(1.)---> Server <---(2.)---> Smartthings Platform

1. Bluetooth connection implemented via Bluez DBus API (C# Code generated via `Tmds.DBus`). Documentation is on [kernel.org](https://git.kernel.org/pub/scm/bluetooth/bluez.git/tree/doc) and [github](https://github.com/RadiusNetworks/bluez/blob/master/doc/device-api.txt). Generated code and some hand-written extensions are in the 'ilamp-communication.dbus' C# project. This currently only works on Unix/Linux systems.

2. Server provides a REST API (details below). This API is referenced in the `deviceHandler.groovy` script in the SmartThings platform. Currently, the API is designed to be used without any security on the same local network as the SmartThings hub. So don't host the server publically (if you don't want the internet to control your lamps)! Note that the script actually runs on the cloud as local processing is not an option for developers like myself (see [the docs](https://community.smartthings.com/t/write-smartapp-to-run-locally/96808/2) and [the forums](https://support.smartthings.com/hc/en-us/articles/209979766-Local-processing)). This means your automations will not work if you loose internet connection. Documentation can be found on [SmartThings](https://docs.smartthings.com/en/latest/ref-docs/device-handler-ref.html)

## Limitations

- Doesn't work without internet
- No Security (use a local hardened network)
- Works for [this lamp](https://www.amazon.de/gp/product/B0774QKF8K/ref=ppx_yo_dt_b_asin_title_o02_s01?ie=UTF8&psc=1).
  It's possible that it works for all devices where the following apps are used/work... Play Store: [i-lamp](https://play.google.com/store/apps/details?id=com.chipsguide.app.colorbluetoothlamp.v3.changda.gp&hl=de), App Store: [i-lamp](https://itunes.apple.com/us/app/i-lamp/id1140789133?mt=8)

## Setup

- Deploy the server (see & edit `run.sh` and `myilamp.service`), its basically a xcopy deployment and easy to integrate into systemd
- Test your server and see if you can control your lamp with the example requests below (in the API section). Replace IP with your deployment.
- [Login to the SmartThings Developer Portal](https://graph.api.smartthings.com/)
- Click "My Locations" and select your Hub location (this might redirect to your local graph: `https://graph-eu01-euwest1.api.smartthings.com`)
- Click "My Device Handlers" and add a new device handler, paste the code from `deviceHandler.groovy` and click 'Create'
- Click "Publish" -> "For Me"
- Click "My Devices" and add a new Device
  - Set a 'Name' and a 'Label' as you like
  - Set 'Debice Network Id *' to 'NULL'
  - Select "Horevo LED Lamp" as type
  - Select "Self-Published" as Version
  - Select the 'Location', the 'Hub' and the 'Group' where the lamp is located.
- Select the newly created device (if not already) and edit the 'Preferences'
  - Set the hostname or IP of the Server (REST). This needs to be accessible/resolvable from the hub. Example `192.168.178.25`
  - Set the port. Default `8585`
  - Set the mac address of the lamp. Example `C9A305FEBD41`

  After clicking save it should look similar to:

  | Name | Type | Value |
  | ---- | ---- | ----- |
  | baseUriHost | string | 192.168.178.25 |
  | baseUriPort | string | 8585 |
  | macAddress | string | C9A305FEBD41 |

- Now you should be able to control your lamp via the SmartThings platform.

## API

The rest API can be used manually if needed.

Generally a request looks like

`http://<IP>:<PORT>/api/lamps/<MAC>?action=<ACTION>&<ARGS>`:

where

- IP: The ip-address of the server
- PORT: The port of the server
- MAC: The mac address of the lamp, without any separators. For example `C9A305FEBD41`.
- ACTION & ARGS: The action to execute:

  | Action | Argument | Type | Range |
  |--------|-----------|------|-------|
  | `set_color` | `brightness` | `double` | 0 - 1 |
  |   | `color` | `RRGGBB Color` | 000000 - FFFFFF |
  | `set_warm_brightness` | `brightness` | `double` | 0 - 1 |
  | `set_warm_color` | `color` | `double` | 0 - 1 |
  | `poweron_white` |  |  | |
  | `poweron_color` |  |  | |
  | `powerooff` |  |  | |

The result is a json with the status of the lamp:

```json
{
    "status":"OK",
    "data": {
        "device": {},
        "uuid":"00001101-0000-1000-8000-00805f9b34fb",
        "profile": {
            "objectPath": {},
            "uuid":"00001101-0000-1000-8000-00805f9b34fb"
        },
        "id":"C9:A3:05:FE:BD:41"
    }
}
```

### Set-Color

#### red
http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=set_color&brightness=0.5&color=FF0000
#### green
http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=set_color&brightness=0.5&color=00FF00
#### blue
http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=set_color&brightness=0.5&color=0000FF

```python
    @staticmethod
    def _get_color_code(brightness, hex_color):
        return "01fe000051811c0000000000000000000d0a02030c%s%s0e0000" % (
                hex2(int(255 * brightness)),
                hex_color
            )
# set_color
# send 01fe000051811c0000000000000000000d0a02030c010000000e0000 # brightness 01RRGGBB
```

### Set-Warm-Brightness

http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=set_warm_brightness&brightness=0.5

```python

    @staticmethod
    def _get_warm_brightness_code(percent):
        assert percent >= 0.0 and percent <= 1.0
        return "01fe00005181180000000000000000000d07010302%s0e00" % (
                hex2(int(16 * percent)) # 0x00 ~ 0x10
            )
# set_warm_brightness
# send 01fe00005181180000000000000000000d07010302000e00 // 00
# send 01fe00005181180000000000000000000d07010302010e00 // 0x01 ~ 0x10 ... hex2(int(16 * percent))
```

### Set-Warm-Color

http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=set_warm_color&color=0.5

```python
    @staticmethod
    def _get_yellow_white_code(percent):
        assert percent >= 0.0 and percent <= 1.0
        return "01fe00005181180000000000000000000d07010303%s0e00" % (
                hex2(int(255 * percent))
            )
# set_warm_color
# send 01fe00005181180000000000000000000d07010303000e00 // 00
# send 01fe00005181180000000000000000000d07010303010e00 // 0x01 ~ 0xFF ... hex2(int(255 * percent))
```

### Set-Mode

http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=poweron_white
http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=poweron_color
http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=poweroff

```python
    @staticmethod
    def _get_open_code(is_color_mode, is_enabled):
        return "01fe00005181180000000000000000000d07%s0301%s0e00" % (
                "02" if is_color_mode else "01",
                "01" if is_enabled else "02"
            )
# send 01fe00005181180000000000000000000d07020301020e00 // true false
# send 01fe00005181180000000000000000000d07010301010e00 // false true
# send 01fe00005181180000000000000000000d07020301010e00 // true true
```