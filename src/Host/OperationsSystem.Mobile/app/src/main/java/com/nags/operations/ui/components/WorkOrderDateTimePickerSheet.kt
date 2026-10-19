package com.nags.operations.ui.components

import android.text.format.DateFormat
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.Button
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.nags.operations.ui.util.parseInUserZone
import java.time.Instant
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.util.Locale

/** Edits a local selection; only Confirm updates the owning form. */
@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
internal fun WorkOrderDateTimePickerSheet(
    label: String,
    initialIso: String,
    flightOffset: ZoneId,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    val initial = remember(initialIso, flightOffset) {
        runCatching { parseInUserZone(initialIso, flightOffset) }
            .getOrElse { ZonedDateTime.now(flightOffset) }
    }
    // Material calendar values describe dates at UTC midnight, not instants in the user's zone.
    val initialMillis = remember(initial) {
        initial.toLocalDate().atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
    }
    val dateState = rememberDatePickerState(initialSelectedDateMillis = initialMillis)
    val system24Hour = DateFormat.is24HourFormat(LocalContext.current)
    val timeState = rememberTimePickerState(initial.hour, initial.minute, system24Hour)
    // Keep restored text and time state in the same format for this editing session.
    val is24Hour = timeState.is24hour
    val focusManager = LocalFocusManager.current
    val locale = LocalConfiguration.current.locales[0] ?: Locale.getDefault()
    val timeFormatter = remember(locale, is24Hour) {
        DateTimeFormatter.ofPattern(if (is24Hour) "HH:mm" else "hh:mm a", locale)
    }
    var hourText by rememberSaveable(stateSaver = TextFieldValue.Saver) {
        mutableStateOf(TextFieldValue(displayHour(initial.hour, is24Hour)))
    }
    var minuteText by rememberSaveable(stateSaver = TextFieldValue.Saver) {
        mutableStateOf(TextFieldValue(twoDigits(initial.minute)))
    }
    val hourRange = if (is24Hour) 0..23 else 1..12
    val hourValid = hourText.text.toIntOrNull() in hourRange
    val minuteValid = minuteText.text.toIntOrNull() in 0..59
    val timeValid = hourValid && minuteValid
    val selectedMillis = dateState.selectedDateMillis
    val pickedDate = Instant.ofEpochMilli(selectedMillis ?: initialMillis)
        .atZone(ZoneOffset.UTC).toLocalDate()
    val dateSummary = pickedDate.format(DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(locale))
    val timeSummary = if (timeValid) LocalTime.of(timeState.hour, timeState.minute).format(timeFormatter) else "— : —"
    // Recompute as the user edits so daylight-saving errors disappear as soon as corrected.
    val selection = if (selectedMillis != null && timeValid) {
        resolveDateTimeSelection(selectedMillis, timeState.hour, timeState.minute, flightOffset)
    } else null

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        sheetMaxWidth = 480.dp,
        modifier = Modifier.fillMaxHeight(0.96f),
        containerColor = MaterialTheme.colorScheme.surface,
    ) {
        Column(Modifier.weight(1f).verticalScroll(rememberScrollState())) {
            Surface(
                color = MaterialTheme.colorScheme.surfaceVariant,
                shape = RoundedCornerShape(20.dp),
                modifier = Modifier.padding(horizontal = 20.dp).fillMaxWidth(),
            ) {
                Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(label, style = MaterialTheme.typography.labelLarge, modifier = Modifier.weight(1f))
                        TextButton(onClick = {
                            focusManager.clearFocus()
                            val now = ZonedDateTime.now(flightOffset)
                            val today = now.toLocalDate().atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
                            if (now.year in dateState.yearRange) {
                                dateState.selectedDateMillis = today
                                dateState.displayedMonthMillis = today
                            }
                            timeState.hour = now.hour
                            timeState.minute = now.minute
                            hourText = TextFieldValue(displayHour(now.hour, is24Hour))
                            minuteText = TextFieldValue(twoDigits(now.minute))
                        }) { Text("Use now") }
                    }
                    Text("$dateSummary · $timeSummary", style = MaterialTheme.typography.titleLarge)
                    Text(
                        "Local time · ${flightOffset.id}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
            DatePicker(
                state = dateState,
                title = null,
                headline = null,
                showModeToggle = false,
                modifier = Modifier.fillMaxWidth(),
                colors = DatePickerDefaults.colors(containerColor = MaterialTheme.colorScheme.surface),
            )
            HorizontalDivider(Modifier.padding(horizontal = 24.dp))
            Surface(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 16.dp),
                shape = RoundedCornerShape(20.dp),
                color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f),
            ) {
                Column(
                    Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Row(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Text(
                            "Select time",
                            style = MaterialTheme.typography.titleMedium,
                            modifier = Modifier.weight(1f).semantics { heading() },
                        )
                        Text(
                            if (is24Hour) "24-hour" else "12-hour",
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    Row(Modifier.fillMaxWidth()) {
                        Text("Hour", Modifier.weight(1f), style = MaterialTheme.typography.labelLarge, textAlign = TextAlign.Center)
                        Spacer(Modifier.width(40.dp))
                        Text("Minute", Modifier.weight(1f), style = MaterialTheme.typography.labelLarge, textAlign = TextAlign.Center)
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                        TimeNumberField(
                            value = hourText,
                            onValueChange = { value ->
                                hourText = value
                                value.text.toIntOrNull()?.takeIf { it in hourRange }?.let { hour ->
                                    timeState.hour = if (is24Hour) hour else hour % 12 + if (timeState.hour >= 12) 12 else 0
                                }
                            },
                            label = "Hour",
                            isError = !hourValid,
                            imeAction = ImeAction.Next,
                            keyboardActions = KeyboardActions(onNext = { focusManager.moveFocus(FocusDirection.Next) }),
                            modifier = Modifier.weight(1f),
                        )
                        Text(
                            ":",
                            style = MaterialTheme.typography.headlineMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.width(24.dp),
                            textAlign = TextAlign.Center,
                        )
                        TimeNumberField(
                            value = minuteText,
                            onValueChange = { value ->
                                minuteText = value
                                value.text.toIntOrNull()?.takeIf { it in 0..59 }?.let { timeState.minute = it }
                            },
                            label = "Minute",
                            isError = !minuteValid,
                            imeAction = ImeAction.Done,
                            keyboardActions = KeyboardActions(onDone = { focusManager.clearFocus() }),
                            modifier = Modifier.weight(1f),
                        )
                    }
                    if (!is24Hour) {
                        SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
                            listOf("AM", "PM").forEachIndexed { index, period ->
                                SegmentedButton(
                                    selected = (timeState.hour >= 12) == (index == 1),
                                    onClick = { timeState.hour = timeState.hour % 12 + index * 12 },
                                    shape = SegmentedButtonDefaults.itemShape(index, 2, RoundedCornerShape(14.dp)),
                                    colors = SegmentedButtonDefaults.colors(
                                        activeContainerColor = MaterialTheme.colorScheme.primary,
                                        activeContentColor = MaterialTheme.colorScheme.onPrimary,
                                        activeBorderColor = MaterialTheme.colorScheme.primary,
                                        inactiveContainerColor = MaterialTheme.colorScheme.surface,
                                        inactiveContentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                                        inactiveBorderColor = MaterialTheme.colorScheme.outline,
                                    ),
                                    icon = {},
                                    label = { Text(period, fontWeight = FontWeight.SemiBold) },
                                    modifier = Modifier.heightIn(min = 48.dp),
                                )
                            }
                        }
                    }
                    val error = when {
                        !hourValid -> if (is24Hour) "Enter an hour from 00 to 23." else "Enter an hour from 01 to 12."
                        !minuteValid -> "Enter minutes from 00 to 59."
                        selection is DateTimeSelection.Invalid -> selection.message
                        else -> null
                    }
                    error?.let {
                        Text(
                            it,
                            color = MaterialTheme.colorScheme.error,
                            style = MaterialTheme.typography.bodySmall,
                            modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
                        )
                    }
                }
            }
        }
        HorizontalDivider()
        FlowRow(
            Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 12.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp, Alignment.End),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            TextButton(onClick = onDismiss, modifier = Modifier.heightIn(min = 48.dp)) { Text("Cancel") }
            Button(
                onClick = {
                    if (selection is DateTimeSelection.Valid) {
                        focusManager.clearFocus()
                        onConfirm(selection.iso)
                    }
                },
                enabled = selection is DateTimeSelection.Valid,
                modifier = Modifier.heightIn(min = 48.dp),
            ) {
                Icon(Icons.Default.Check, contentDescription = null)
                Spacer(Modifier.width(8.dp))
                Text("Confirm")
            }
        }
    }
}

private fun twoDigits(value: Int): String = value.toString().padStart(2, '0')

private fun displayHour(hour: Int, is24Hour: Boolean): String =
    twoDigits(if (is24Hour) hour else (hour % 12).let { if (it == 0) 12 else it })

@Composable
private fun TimeNumberField(
    value: TextFieldValue,
    onValueChange: (TextFieldValue) -> Unit,
    label: String,
    isError: Boolean,
    imeAction: ImeAction,
    keyboardActions: KeyboardActions,
    modifier: Modifier = Modifier,
) {
    var focused by remember { mutableStateOf(false) }
    LaunchedEffect(focused) {
        // Select after the focus tap has positioned its cursor, so typing replaces both digits.
        if (focused) onValueChange(value.copy(selection = TextRange(0, value.text.length)))
    }
    OutlinedTextField(
        value = value,
        onValueChange = { next ->
            if (next.text.length <= 2 && next.text.all(Char::isDigit)) onValueChange(next)
        },
        modifier = modifier
            .heightIn(min = 72.dp)
            .onFocusChanged { focused = it.isFocused }
            .semantics { contentDescription = label },
        textStyle = MaterialTheme.typography.headlineLarge.copy(
            textAlign = TextAlign.Center,
            fontWeight = FontWeight.SemiBold,
            fontFeatureSettings = "tnum",
        ),
        shape = RoundedCornerShape(16.dp),
        colors = OutlinedTextFieldDefaults.colors(
            focusedContainerColor = MaterialTheme.colorScheme.surface,
            unfocusedContainerColor = MaterialTheme.colorScheme.surface,
        ),
        singleLine = true,
        isError = isError,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = imeAction),
        keyboardActions = keyboardActions,
    )
}
