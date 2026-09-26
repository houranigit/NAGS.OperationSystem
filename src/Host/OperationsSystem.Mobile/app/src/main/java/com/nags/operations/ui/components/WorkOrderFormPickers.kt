package com.nags.operations.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.SecondaryTabRow
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.allowedPerformedServiceOptions
import com.nags.operations.ui.util.formatIsoForDisplay
import com.nags.operations.ui.util.formatIsoForDateTimeField
import com.nags.operations.ui.util.parseOffsetDateTime
import java.time.Instant
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZoneOffset

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
    var dialogOpen by remember { mutableStateOf(false) }

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
        WorkOrderOffsetDateTimePickerDialog(
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

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun WorkOrderOffsetDateTimePickerDialog(
    initialIso: String,
    flightOffset: ZoneId,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    val initialZoned = remember(initialIso, flightOffset) {
        try {
            parseOffsetDateTime(initialIso).atZoneSameInstant(flightOffset)
        } catch (_: Exception) {
            java.time.ZonedDateTime.now(flightOffset)
        }
    }

    // Material's DatePicker represents a calendar date as UTC midnight, not as the selected
    // instant. Seeding it this way prevents positive/negative zones from shifting the date.
    val initialMillis = remember(initialZoned) {
        initialZoned.toLocalDate().atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
    }
    val datePickerState = rememberDatePickerState(initialSelectedDateMillis = initialMillis)
    val timePickerState = rememberTimePickerState(
        initialHour = initialZoned.hour,
        initialMinute = initialZoned.minute,
        is24Hour = false,
    )

    var tabIndex by remember { mutableIntStateOf(0) }
    var timeZoneError by remember { mutableStateOf<String?>(null) }

    DatePickerDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            TextButton(
                onClick = {
                    val selectedMillis = datePickerState.selectedDateMillis ?: initialMillis
                    val pickedDate = Instant.ofEpochMilli(selectedMillis)
                        .atZone(ZoneOffset.UTC)
                        .toLocalDate()
                    val pickedTime = LocalTime.of(timePickerState.hour, timePickerState.minute)
                    val combined = LocalDateTime.of(pickedDate, pickedTime)
                    val validOffsets = flightOffset.rules.getValidOffsets(combined)
                    when (validOffsets.size) {
                        0 -> timeZoneError = "That local time does not exist because the clock moves forward. Choose another time."
                        1 -> onConfirm(combined.atOffset(validOffsets.single()).toString())
                        else -> timeZoneError = "That local time occurs twice because the clock moves back. Choose a time outside the repeated hour."
                    }
                },
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Default.Check, contentDescription = null)
                    Spacer(Modifier.width(6.dp))
                    Text("Confirm")
                }
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        },
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            SecondaryTabRow(selectedTabIndex = tabIndex) {
                Tab(
                    selected = tabIndex == 0,
                    onClick = { tabIndex = 0 },
                    icon = { Icon(Icons.Default.CalendarMonth, contentDescription = null) },
                    text = { Text("Date") },
                )
                Tab(
                    selected = tabIndex == 1,
                    onClick = { tabIndex = 1 },
                    icon = { Icon(Icons.Default.Schedule, contentDescription = null) },
                    text = { Text("Time") },
                )
            }
            when (tabIndex) {
                0 -> DatePicker(
                    state = datePickerState,
                    title = null,
                    headline = null,
                    showModeToggle = false,
                    modifier = Modifier.padding(top = 4.dp),
                )
                else -> Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 24.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    TimePicker(state = timePickerState)
                }
            }
            timeZoneError?.let { message ->
                Text(
                    text = message,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodySmall,
                    modifier = Modifier.padding(horizontal = 24.dp, vertical = 8.dp),
                )
            }
        }
    }
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
