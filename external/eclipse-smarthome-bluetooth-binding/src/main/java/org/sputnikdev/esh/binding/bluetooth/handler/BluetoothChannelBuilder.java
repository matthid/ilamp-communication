package org.sputnikdev.esh.binding.bluetooth.handler;

import org.eclipse.smarthome.core.thing.Channel;
import org.eclipse.smarthome.core.thing.ChannelUID;
import org.eclipse.smarthome.core.thing.binding.builder.ChannelBuilder;
import org.eclipse.smarthome.core.thing.type.ChannelTypeUID;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.sputnikdev.bluetooth.URL;
import org.sputnikdev.bluetooth.gattparser.spec.Field;
import org.sputnikdev.bluetooth.gattparser.spec.FieldFormat;
import org.sputnikdev.esh.binding.bluetooth.BluetoothBindingConstants;
import org.sputnikdev.esh.binding.bluetooth.internal.BluetoothUtils;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;


/**
 * A utility class for creating dynamic channels for discovered GATT characteristics.
 *
 * @author Vlad Kolotov
 */
class BluetoothChannelBuilder {

    private Logger logger = LoggerFactory.getLogger(BluetoothChannelBuilder.class);
    private static final String CHANNEL_TYPE_NAME_PATTERN = "characteristic-%s-%s-%s-%s";

    private final BluetoothHandler handler;

    BluetoothChannelBuilder(BluetoothHandler handler) {
        this.handler = handler;
    }


    List<Channel> buildChannels(URL url, List<Field> fields, boolean advanced, boolean readOnly) {
        List<Channel> channels = new ArrayList<>();

        Map<String, List<Field>> fieldsMapping = fields.stream().collect(Collectors.groupingBy(Field::getName));

        String label = null;
        // check if the characteristic has only on field, if so use its name as label
        if (handler.getParser().getFields(url.getCharacteristicUUID()).size() == 1) {
            label = handler.getParser().getCharacteristic(url.getCharacteristicUUID()).getName();
        }

        for (List<Field> fieldList : fieldsMapping.values()) {
            if (fieldList.size() > 1) {
                if (fieldList.get(0).isFlagField() || fieldList.get(0).isOpCodesField()) {
                    logger.debug("Skipping flags/op codes field: {}.", url);
                } else {
                    logger.warn("Multiple fields with the same name found: {} / {}. Skipping these fields.",
                            url, fieldList.get(0).getName());
                }
                continue;
            }
            Field field = fieldList.get(0);
            if (isFieldSupported(field)) {
                channels.add(buildFieldChannel(url, field,
                        label == null ? field.getName() : label, advanced, readOnly));
            } else {
                logger.warn("GATT field is not supported: {} / {} / {}", url, field.getName(), field.getFormat());
            }
        }
        return channels;
    }

    private boolean isFieldSupported(Field field) {
        return field.getFormat() != null;
    }

    protected Channel buildBinaryChannel(URL characteristicURL, boolean advanced, boolean readOnly) {
        ChannelUID channelUID = new ChannelUID(handler.getThing().getUID(),
                BluetoothUtils.getChannelUID(characteristicURL));

        String channelType = getChannelType(advanced, readOnly);

        ChannelTypeUID channelTypeUID = new ChannelTypeUID(BluetoothBindingConstants.BINDING_ID, channelType);
        return ChannelBuilder.create(channelUID, "String")
                .withType(channelTypeUID)
                .withLabel(characteristicURL.getCharacteristicUUID())
                .build();
    }

    private Channel buildFieldChannel(URL characteristicURL, Field field, String label,
                                      boolean advanced, boolean readOnly) {
        String acceptedType = getAcceptedItemType(field);
        if (acceptedType == null) {
            // unknown field format
            return null;
        }

        URL channelURL = characteristicURL.copyWithField(field.getName());
        logger.debug("Building a new channel for a field: {}", channelURL);

        ChannelUID channelUID = new ChannelUID(handler.getThing().getUID(), BluetoothUtils.getChannelUID(channelURL));

        String channelType = String.format(CHANNEL_TYPE_NAME_PATTERN,
                advanced ? "advncd" : "simple",
                readOnly ? "readable" : "writable",
                characteristicURL.getCharacteristicUUID(),
                BluetoothUtils.encodeFieldID(field));

        ChannelTypeUID channelTypeUID = new ChannelTypeUID(BluetoothBindingConstants.BINDING_ID, channelType);
        return ChannelBuilder.create(channelUID, getAcceptedItemType(field))
                .withType(channelTypeUID)
                .withProperties(getFieldProperties(characteristicURL, field))
                .withLabel(label)
                .build();
    }

    private static String getChannelType(boolean advanced, boolean readOnly) {
        // making channel type that should match one of channel types from the "thing-types.xml" config file, these are:
        // characteristic-advanced-readonly-field
        // characteristic-advanced-editable-field
        // characteristic-readonly-field
        // characteristic-editable-field

        return String.format("characteristic%s%s-field",
                advanced ? "-advanced" : "", readOnly ? "-readonly" : "-editable");
    }

    private static Map<String, String> getFieldProperties(URL characteristicURL, Field field) {
        Map<String, String> properties = new HashMap<>();
        properties.put(BluetoothBindingConstants.PROPERTY_FIELD_NAME, field.getName());
        properties.put(BluetoothBindingConstants.PROPERTY_SERVICE_UUID, characteristicURL.getServiceUUID());
        properties.put(BluetoothBindingConstants.PROPERTY_CHARACTERISTIC_UUID,
                characteristicURL.getCharacteristicUUID());
        return properties;
    }

    private String getAcceptedItemType(Field field) {
        FieldFormat format = field.getFormat();
        if (format == null) {
            // unknown format
            return null;
        }
        switch (field.getFormat().getType()) {
            case BOOLEAN: return "Switch";
            case UINT:
            case SINT:
            case FLOAT_IEE754:
            case FLOAT_IEE11073: return "Number";
            case UTF8S:
            case UTF16S: return "String";
            case STRUCT: return "String";
            // unsupported format
            default: return null;
        }
    }

}
