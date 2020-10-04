
# DBUS Codegen

dotnet tool restore
cd src/ilamp-communication.dbus
rm bluez.DBus.cs
dotnet dotnet-dbus codegen --bus system --service org.bluez --public
