Ressources

https://www.defcon.org/images/defcon-22/dc-22-presentations/Strazzere-Sawyer/DEFCON-22-Strazzere-and-Sawyer-Android-Hacker-Protection-Level-UPDATED.pdf

https://github.com/strazzere/android-unpacker

https://loccs.sjtu.edu.cn/~romangol/publications/jss18.pdf
https://github.com/UchihaL/AppSpear


https://github.com/zyq8709/DexHunter


## API


Python example:
 
``` python
from audiolight.bulb import Bulb

mac_addr = 'C9:A3:05:FE:BD:41'

bulb = Bulb(mac_addr)
bulb.connect()

bulb.set_mode(True, True) # To color mode & enabled
bulb.set_color(1.0, '0000ff') # Set color to blue
```

Connect


```python
        self._sock.connect((self._host, self._port))

        # First of all, send '01234567'
        self._send_hex_string('3031323334353637')

        # Send maybe a query message or a heartbeat message.
        # Official app do this about once per second.
        self._send_hex_string('01fe0000510210000000008000000080')
        self._recv_bytes(16)
        self._is_connected = True
```

Set-Color

# red
http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=set_color&brightness=0.5&color=FF0000
# green
http://192.168.178.25:8585/api/lamps/C9A305FEBD41?action=set_color&brightness=0.5&color=00FF00
# blue
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

Set-Warm-Brightness

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

Set-Warm-Color

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

Set-Mode

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
# send 01fe00005181180000000000000000000d07020301020e00 //true false
# send 01fe00005181180000000000000000000d07010301010e00 // false true 
# send 01fe00005181180000000000000000000d07020301010e00 // true true 
```