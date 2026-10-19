package com.nags.operations.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.allowedPerformedServiceOptions
import com.nags.operations.ui.util.formatIsoForDisplay
import com.nags.operations.ui.util.formatIsoForDateTimeField
import java.time.ZoneId

/** Same flight banner as [FlightDetailsActionsSheet] / [FlightOverviewBanner] for a consistent look. */
@Composable
fun WorkOrderFlightSummaryCard(
    customerName: String,
    customerIataCode: String,
    stationCode: String,
    staIso: String,
    stdIso: String,
    flightNumber: String? = null,
    aircraftModel: String? = null,
    operationTypeCode: String = "",
    modifier: Modifier = Modifier,
) {
    FlightOverviewBanner(
        customerIataCode = customerIataCode,
        customerName = customerName,
        stationCode = stationCode,
        operationTypeCode = operationTypeCode,
        flightNumber = flightNumber?.takeIf { it.isNotBlank() },
        aircraftModel = aircraftModel,
        staDisplay = formatIsoForDisplay(staIso),
        stdDisplay = formatIsoForDisplay(stdIso),
        modifier = modifier,
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WorkOrderDateTimePickerField(
    iso: String,
    label: String,
    placeholder: String,
    flightOffset: ZoneId,
    /** When [iso] is blank, the dialog opens seeded from this value (e.g. STA). */
    defaultInitialIso: String,
    onIsoConfirmed: (String) -> Unit,
    modifier: Modifier = Modifier,
    isError: Boolean = false,
    supportingText: @Composable (() -> Unit)? = null,
    enabled: Boolean = true,
    compact: Boolean = false,
) {
    var dialogOpen by rememberSaveable { mutableStateOf(false) }

    Box(
        modifier = modifier
            .fillMaxWidth()
            .height(IntrinsicSize.Min),
    ) {
        OutlinedTextField(
            value = iso.takeIf { it.isNotBlank() }?.let {
                if (compact) formatIsoForDateTimeField(it, flightOffset) else formatIsoForDisplay(it, flightOffset)
            }.orEmpty(),
            onValueChange = {},
            readOnly = true,
            enabled = enabled,
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(14.dp),
            isError = isError,
            supportingText = supportingText,
            minLines = if (compact) 2 else 1,
            label = { Text(label) },
            placeholder = { Text(placeholder) },
            trailingIcon = {
                Icon(Icons.Default.CalendarMonth, contentDescription = null)
            },
        )
        Box(
            Modifier
                .fillMaxSize()
                .clickable(
                    enabled = enabled,
                    indication = null,
                    interactionSource = remember { MutableInteractionSource() },
                ) { dialogOpen = true },
        )
    }

    if (dialogOpen) {
        WorkOrderDateTimePickerSheet(
            label = label,
            initialIso = iso.takeIf { it.isNotBlank() } ?: defaultInitialIso,
            flightOffset = flightOffset,
            onDismiss = { dialogOpen = false },
            onConfirm = { confirmed ->
                dialogOpen = false
                onIsoConfirmed(confirmed)
            },
        )
    }
}

/** Keep period boxes aligned, wrapping the pair when the available width is too narrow. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun WorkOrderDateTimeRange(
    fromIso: String,
    toIso: String,
    flightOffset: ZoneId,
    defaultInitialIso: String,
    onFromConfirmed: (String) -> Unit,
    onToConfirmed: (String) -> Unit,
    modifier: Modifier = Modifier,
    toInitialIso: String = fromIso.ifBlank { defaultInitialIso },
    fromIsError: Boolean = false,
    toIsError: Boolean = false,
    fromSupportingText: @Composable (() -> Unit)? = null,
    toSupportingText: @Composable (() -> Unit)? = null,
    optionalTo: Boolean = false,
) {
    val textMeasurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val textStyle = MaterialTheme.typography.bodyLarge
    val values = listOf(fromIso, toIso).map { it.ifBlank { defaultInitialIso } }
    val textWidth = values.flatMap { formatIsoForDateTimeField(it, flightOffset).lines() }
        .maxOf { textMeasurer.measure(it, textStyle, softWrap = false).size.width }
    // Allow the text's actual locale/font size plus the field padding and calendar icon.
    val fieldWidth = with(density) { textWidth.toDp() } + 80.dp
    FlowRow(
        modifier = modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
        maxItemsInEachRow = 2,
    ) {
        // width supplies FlowRow's intrinsic wrapping width; weight fills the available row.
        val fieldModifier = Modifier.weight(1f).width(fieldWidth)
        WorkOrderDateTimePickerField(
            iso = fromIso,
            label = "From",
            placeholder = "Start date & time",
            flightOffset = flightOffset,
            defaultInitialIso = defaultInitialIso,
            onIsoConfirmed = onFromConfirmed,
            modifier = fieldModifier,
            isError = fromIsError,
            supportingText = fromSupportingText,
            compact = true,
        )
        WorkOrderDateTimePickerField(
            iso = toIso,
            label = "To",
            placeholder = if (optionalTo) "Open-ended" else "End date & time",
            flightOffset = flightOffset,
            defaultInitialIso = toInitialIso,
            onIsoConfirmed = onToConfirmed,
            modifier = fieldModifier,
            isError = toIsError,
            supportingText = toSupportingText,
            compact = true,
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WorkOrderAtaPickerField(
    ataIso: String,
    staIso: String,
    flightOffset: ZoneId,
    onAtaConfirmed: (String) -> Unit,
    modifier: Modifier = Modifier,
    isError: Boolean = false,
    supportingText: @Composable (() -> Unit)? = null,
) {
    WorkOrderDateTimePickerField(
        iso = ataIso,
        label = "Actual arrival (ATA)",
        placeholder = "Tap to set arrival date & time",
        flightOffset = flightOffset,
        defaultInitialIso = staIso,
        onIsoConfirmed = onAtaConfirmed,
        modifier = modifier,
        isError = isError,
        supportingText = supportingText,
    )
}

@Composable
fun AircraftTypePicker(
    selectedId: String?,
    options: List<AircraftTypeEntity>,
    onSelected: (AircraftTypeEntity) -> Unit,
    onCleared: () -> Unit,
    readOnly: Boolean = false,
    isError: Boolean = false,
    supportingText: @Composable (() -> Unit)? = null,
) {
    val selected = options.firstOrNull { it.aircraftTypeId == selectedId }
    InlineSearchableDropdownField(
        label = "Aircraft type",
        selectedText = selected?.model.orEmpty(),
        placeholder = "Type to search aircraft type",
        options = options,
        renderOption = { it.model },
        onSelect = onSelected,
        onClearSelection = onCleared,
        hasSelection = selectedId != null,
        readOnly = readOnly,
        isError = isError,
        supportingText = supportingText,
    )
}

@Composable
fun WorkOrderServicePicker(
    selectedId: String?,
    selectedNameFallback: String? = null,
    options: List<ServiceEntity>,
    onSelected: (ServiceEntity) -> Unit,
    onCleared: () -> Unit,
    readOnly: Boolean = false,
    modifier: Modifier = Modifier,
    isError: Boolean = false,
    supportingText: @Composable (() -> Unit)? = null,
) {
    val selectableOptions = options.allowedPerformedServiceOptions()
    val selected = options.firstOrNull { it.serviceId == selectedId }
    InlineSearchableDropdownField(
        label = "Service type",
        selectedText = selected?.name ?: selectedNameFallback.orEmpty(),
        placeholder = "Type to search services",
        options = selectableOptions,
        renderOption = { it.name },
        onSelect = onSelected,
        onClearSelection = onCleared,
        hasSelection = selectedId != null,
        readOnly = readOnly || selectableOptions.isEmpty(),
        modifier = modifier,
        isError = isError,
        supportingText = supportingText,
    )
}
