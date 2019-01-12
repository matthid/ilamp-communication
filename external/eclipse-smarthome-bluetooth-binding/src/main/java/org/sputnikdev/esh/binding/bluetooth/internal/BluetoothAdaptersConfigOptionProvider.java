package org.sputnikdev.esh.binding.bluetooth.internal;

import org.eclipse.smarthome.config.core.ConfigOptionProvider;
import org.eclipse.smarthome.config.core.ParameterOption;
import org.eclipse.smarthome.core.items.ItemRegistry;
import org.eclipse.smarthome.core.thing.Thing;
import org.eclipse.smarthome.core.thing.ThingRegistry;
import org.osgi.service.component.annotations.Component;
import org.osgi.service.component.annotations.Reference;
import org.osgi.service.component.annotations.ReferenceCardinality;
import org.sputnikdev.bluetooth.manager.BluetoothManager;
import org.sputnikdev.bluetooth.manager.DiscoveredAdapter;
import org.sputnikdev.esh.binding.bluetooth.BluetoothBindingConstants;

import java.net.URI;
import java.util.Collection;
import java.util.Locale;
import java.util.Set;
import java.util.stream.Collectors;
import java.util.stream.Stream;

/**
 * {@link ConfigOptionProvider} that provides a list of available bluetooth adapters.
 * The following param names are used to return adapters for:
 * <ul>
 *     <li>preferredBluetoothAdapter</li>
 *     <li>bluetoothAdapter</li>
 *     <li>bluetooth-adapter</li>
 * </ul>
 *
 * @author Vlad Kolotov
 */
@Component(immediate = true, service = ConfigOptionProvider.class)
public class BluetoothAdaptersConfigOptionProvider implements ConfigOptionProvider {

    private static final Set<String> PARAM_NAMES =
            Stream.of("preferredBluetoothAdapter", "bluetoothAdapter",  "bluetooth-adapter")
                    .collect(Collectors.toSet());

    private BluetoothManager bluetoothManager;
    private ThingRegistry thingRegistry;

    @Override
    public Collection<ParameterOption> getParameterOptions(URI uri, String param, Locale locale) {
        if (PARAM_NAMES.contains(param) && bluetoothManager != null) {
            return bluetoothManager.getDiscoveredAdapters().stream()
                    .filter(adapter -> !adapter.isCombined())
                    .map(this::convert)
                    .collect(Collectors.toSet());
        }
        return null;
    }

    @Reference(cardinality = ReferenceCardinality.MANDATORY)
    public void setBluetoothManager(BluetoothManager bluetoothManager) {
        this.bluetoothManager = bluetoothManager;
    }

    public void unsetBluetoothManager(BluetoothManager bluetoothManager) {
        this.bluetoothManager = null;
    }

    @Reference(cardinality = ReferenceCardinality.MANDATORY)
    public void setThingRegistry(ThingRegistry thingRegistry) {
        this.thingRegistry = thingRegistry;
    }

    public void unsetThingRegistry(ItemRegistry thingRegistry) {
        this.thingRegistry = null;
    }

    private ParameterOption convert(DiscoveredAdapter adapter) {
        String displayValue = adapter.getURL().getAdapterAddress();
        Thing adapterThing = thingRegistry.get(BluetoothUtils.getAdapterUID(adapter.getURL()));
        if (adapterThing != null
                && !BluetoothBindingConstants.DEFAULT_ADAPTERS_LOCATION.equals(adapterThing.getLocation())) {
            displayValue += " (" + adapterThing.getLocation() + ")";
        } else if (adapter.getAlias() != null) {
            displayValue += " (" + adapter.getAlias() + ") ";
        } else {
            displayValue += " (" + adapter.getName() + ") ";
        }
        return new ParameterOption(adapter.getURL().getAdapterAddress(), displayValue);
    }
}
