package com.nags.operations.ui.flights

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DatePickerDefaults
import androidx.compose.material3.DisplayMode
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimeInput
import androidx.compose.material3.TimePickerDefaults
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.nags.operations.R
import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.ui.components.CustomerLogo
import com.nags.operations.ui.util.formatIsoDateForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import kotlinx.coroutines.flow.Flow

/** Temporary screen state only: this form has no submission or persistence dependency. */
@Composable
internal fun FlightArrivalForm(
    flight: FlightDetail,
    aircraftTypes: List<AircraftTypeCatalogItem>,
    stationStaff: List<StaffCatalogItem>,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onClose: () -> Unit,
) {
    var date by rememberSaveable { mutableStateOf("") }
    var time by rememberSaveable { mutableStateOf("") }
    var aircraftId by rememberSaveable { mutableStateOf("") }
    var tailNumber by rememberSaveable { mutableStateOf("") }
    var employeeId by rememberSaveable { mutableStateOf("") }
    var reference by rememberSaveable { mutableStateOf("") }
    var remarks by rememberSaveable { mutableStateOf("") }
    var picker by rememberSaveable { mutableStateOf<String?>(null) }
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    val hasChanges = listOf(date, time, aircraftId, tailNumber, employeeId, reference, remarks).any { it.isNotEmpty() }
    val requestClose = { if (hasChanges) confirmDiscard = true else onClose() }
    val locale = LocalLocale.current.platformLocale
    val title = stringResource(R.string.arrival_title)
    val aircraftChoices = remember(aircraftTypes) {
        aircraftTypes.map { ArrivalChoice(it.id, it.model, it.manufacturer) }.sortedBy { it.title }
    }
    val employeeChoices = remember(stationStaff, flight.stationId) {
        stationStaff.filter { it.stationId == flight.stationId }
            .map { ArrivalChoice(it.id, it.fullName, it.employeeId) }.sortedBy { it.title }
    }
    Dialog(onDismissRequest = requestClose,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.surface) {
            Column(Modifier.fillMaxSize().safeDrawingPadding().imePadding().semantics { paneTitle = title },
                horizontalAlignment = Alignment.CenterHorizontally) {
                Row(Modifier.widthIn(max = 800.dp).fillMaxWidth().padding(horizontal = 8.dp, vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    IconButton(onClick = requestClose, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.account_back))
                    }
                    Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold,
                        modifier = Modifier.weight(1f).padding(start = 8.dp), maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                Column(Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState()),
                    horizontalAlignment = Alignment.CenterHorizontally) {
                    Column(Modifier.widthIn(max = 760.dp).fillMaxWidth().padding(horizontal = 20.dp, vertical = 20.dp),
                        verticalArrangement = Arrangement.spacedBy(20.dp)) {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                                CustomerLogo(flight.customerId, loadCustomerLogo, Modifier.size(40.dp))
                                Column(Modifier.weight(1f)) {
                                    Text(flight.flightNumber, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold,
                                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                                    Text(flight.customerName, style = MaterialTheme.typography.bodyMedium,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                }
                            }
                            Text(stringResource(R.string.arrival_context_schedule, flight.stationIata,
                                "${formatIsoDateForDisplay(flight.scheduledArrivalUtc)} ${formatIsoTimeForDisplay(flight.scheduledArrivalUtc)}"), style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            Text(stringResource(R.string.arrival_ata), style = MaterialTheme.typography.titleSmall,
                                fontWeight = FontWeight.SemiBold, modifier = Modifier.semantics { heading() })
                            ArrivalFieldPair(minWidth = 340, first = { modifier ->
                                ArrivalChoiceField(stringResource(R.string.arrival_date),
                                    if (date.isBlank()) "" else LocalDate.parse(date).format(
                                        DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(locale)),
                                    stringResource(R.string.arrival_select_date), Icons.Outlined.CalendarMonth,
                                    { picker = "date" }, modifier)
                            }, second = { modifier ->
                                ArrivalChoiceField(stringResource(R.string.arrival_time), time,
                                    stringResource(R.string.arrival_select_time), Icons.Outlined.Schedule,
                                    { picker = "time" }, modifier)
                            })
                            Text(stringResource(R.string.arrival_local_time, ZoneId.systemDefault().id),
                                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        ArrivalFieldPair(first = { modifier ->
                            ArrivalChoiceField(stringResource(R.string.arrival_aircraft_type),
                                aircraftChoices.firstOrNull { it.id == aircraftId }?.title.orEmpty(),
                                stringResource(R.string.arrival_select_aircraft), Icons.Outlined.ExpandMore,
                                { picker = "aircraft" }, modifier)
                        }, second = { modifier ->
                            ArrivalTextField(stringResource(R.string.arrival_tail_number), tailNumber, { tailNumber = it },
                                stringResource(R.string.arrival_tail_hint), modifier, required = true,
                                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next))
                        })
                        ArrivalFieldPair(first = { modifier ->
                            ArrivalChoiceField(stringResource(R.string.arrival_received_by),
                                employeeChoices.firstOrNull { it.id == employeeId }?.title.orEmpty(),
                                stringResource(R.string.arrival_select_employee), Icons.Outlined.ExpandMore,
                                { picker = "employee" }, modifier)
                        }, second = { modifier ->
                            ArrivalTextField(stringResource(R.string.arrival_reference), reference, { reference = it },
                                stringResource(R.string.arrival_reference_hint), modifier)
                        })
                        ArrivalTextField(stringResource(R.string.arrival_remarks), remarks, { remarks = it },
                            stringResource(R.string.arrival_remarks_hint), multiline = true,
                            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences))
                        Text(stringResource(R.string.arrival_required_hint), style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                Column(Modifier.widthIn(max = 760.dp).fillMaxWidth().padding(horizontal = 20.dp, vertical = 12.dp),
                    verticalArrangement = Arrangement.spacedBy(4.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Button(onClick = {}, enabled = false, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
                        shape = RoundedCornerShape(14.dp)) { Text(stringResource(R.string.arrival_save), fontWeight = FontWeight.SemiBold) }
                    Text(stringResource(R.string.arrival_save_unavailable), style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
    when (picker) {
        "date" -> ArrivalDatePicker(date, onDismiss = { picker = null }) { date = it; picker = null }
        "time" -> ArrivalTimePicker(time, onDismiss = { picker = null }) { time = it; picker = null }
        "aircraft", "employee" -> {
            val aircraft = picker == "aircraft"
            ArrivalChoicePicker(title = stringResource(if (aircraft) R.string.arrival_aircraft_type else R.string.arrival_received_by),
                searchLabel = stringResource(if (aircraft) R.string.arrival_search_aircraft else R.string.arrival_search_employee),
                emptyLabel = stringResource(if (aircraft) R.string.arrival_no_aircraft else R.string.arrival_no_staff),
                options = if (aircraft) aircraftChoices else employeeChoices,
                selectedId = if (aircraft) aircraftId else employeeId,
                onDismiss = { picker = null }, onSelect = {
                    if (aircraft) aircraftId = it else employeeId = it
                    picker = null
                })
        }
    }
    if (confirmDiscard) AlertDialog(onDismissRequest = { confirmDiscard = false }, containerColor = MaterialTheme.colorScheme.surface, tonalElevation = 0.dp,
        title = { Text(stringResource(R.string.arrival_discard_title)) },
        text = { Text(stringResource(R.string.arrival_discard_hint)) },
        confirmButton = { TextButton(onClick = onClose) { Text(stringResource(R.string.arrival_discard), color = MaterialTheme.colorScheme.error) } },
        dismissButton = { TextButton(onClick = { confirmDiscard = false }) { Text(stringResource(R.string.arrival_keep_editing)) } })
}

@Composable
private fun ArrivalFieldPair(
    minWidth: Int = 560,
    first: @Composable (Modifier) -> Unit,
    second: @Composable (Modifier) -> Unit,
) {
    val fontScale = LocalDensity.current.fontScale
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        if (maxWidth >= minWidth.dp * fontScale) {
            Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                first(Modifier.weight(1f)); second(Modifier.weight(1f))
            }
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(20.dp)) {
                first(Modifier.fillMaxWidth()); second(Modifier.fillMaxWidth())
            }
        }
    }
}

@Composable
private fun ArrivalChoiceField(label: String, value: String, placeholder: String, icon: ImageVector,
    onClick: () -> Unit, modifier: Modifier = Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("$label *", style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold)
        Surface(onClick = onClick, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp).semantics { contentDescription = label },
            shape = RoundedCornerShape(14.dp), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
            color = MaterialTheme.colorScheme.surface) {
            Row(Modifier.padding(horizontal = 16.dp, vertical = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Text(value.ifBlank { placeholder }, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge,
                    color = if (value.isBlank()) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
                    maxLines = 2, overflow = TextOverflow.Ellipsis)
                Icon(icon, null, Modifier.size(20.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
private fun ArrivalTextField(label: String, value: String, onValueChange: (String) -> Unit, placeholder: String,
    modifier: Modifier = Modifier, required: Boolean = false, multiline: Boolean = false,
    keyboardOptions: KeyboardOptions = KeyboardOptions(imeAction = ImeAction.Next)) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(if (required) "$label *" else label, style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold)
        OutlinedTextField(value, onValueChange, modifier = Modifier.fillMaxWidth().semantics { contentDescription = label },
            placeholder = { Text(placeholder) }, shape = RoundedCornerShape(14.dp),
            singleLine = !multiline, minLines = if (multiline) 3 else 1, maxLines = if (multiline) 5 else 1,
            keyboardOptions = keyboardOptions)
    }
}

private data class ArrivalChoice(val id: String, val title: String, val subtitle: String)

@Composable
private fun ArrivalChoicePicker(title: String, searchLabel: String, emptyLabel: String, options: List<ArrivalChoice>,
    selectedId: String, onDismiss: () -> Unit, onSelect: (String) -> Unit) {
    var search by rememberSaveable { mutableStateOf("") }
    val filtered = remember(options, search) {
        options.filter { it.title.contains(search.trim(), ignoreCase = true) || it.subtitle.contains(search.trim(), ignoreCase = true) }
    }
    Dialog(onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        Box(Modifier.fillMaxSize().safeDrawingPadding().imePadding().padding(16.dp), contentAlignment = Alignment.Center) {
            Surface(Modifier.widthIn(max = 560.dp).fillMaxWidth().fillMaxHeight(0.85f),
                shape = RoundedCornerShape(24.dp), color = MaterialTheme.colorScheme.surface) {
                Column {
                    Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 8.dp, top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                        IconButton(onDismiss, Modifier.size(48.dp)) { Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel)) }
                    }
                    OutlinedTextField(search, { search = it }, modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 12.dp),
                        label = { Text(searchLabel) }, singleLine = true, shape = RoundedCornerShape(14.dp),
                        trailingIcon = if (search.isNotEmpty()) {{ IconButton(onClick = { search = "" }) {
                            Icon(Icons.Outlined.Close, stringResource(R.string.flights_search_clear))
                        } }} else null)
                    if (filtered.isEmpty()) {
                        Text(if (options.isEmpty()) emptyLabel else stringResource(R.string.arrival_no_results),
                            Modifier.padding(20.dp), style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    } else {
                        LazyColumn(Modifier.weight(1f).fillMaxWidth()) {
                            items(filtered, key = { it.id }) { option ->
                                Row(Modifier.fillMaxWidth().heightIn(min = 64.dp).selectable(
                                    selected = option.id == selectedId, role = Role.RadioButton, onClick = { onSelect(option.id) })
                                    .padding(horizontal = 20.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                                    Column(Modifier.weight(1f)) {
                                        Text(option.title, style = MaterialTheme.typography.bodyLarge, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                        if (option.subtitle.isNotBlank()) Text(option.subtitle, style = MaterialTheme.typography.bodySmall,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                    }
                                    RadioButton(option.id == selectedId, onClick = null)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ArrivalDatePicker(selected: String, onDismiss: () -> Unit, onSelect: (String) -> Unit) {
    // DatePicker's UTC midnight identifies a calendar date, not an actual arrival instant.
    val initial = (selected.takeIf { it.isNotBlank() }?.let(LocalDate::parse) ?: LocalDate.now())
        .atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
    val state = rememberDatePickerState(initialSelectedDateMillis = initial, initialDisplayMode = DisplayMode.Input)
    DatePickerDialog(onDismissRequest = onDismiss, tonalElevation = 0.dp,
        colors = DatePickerDefaults.colors(containerColor = MaterialTheme.colorScheme.surface),
        confirmButton = { TextButton(onClick = {
            state.selectedDateMillis?.let { onSelect(Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate().toString()) }
        }, enabled = state.selectedDateMillis != null) { Text(stringResource(android.R.string.ok)) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(android.R.string.cancel)) } }) {
        DatePicker(state, colors = DatePickerDefaults.colors(containerColor = MaterialTheme.colorScheme.surface))
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ArrivalTimePicker(selected: String, onDismiss: () -> Unit, onSelect: (String) -> Unit) {
    val initial = selected.takeIf { it.isNotBlank() }?.let(LocalTime::parse) ?: LocalTime.now()
    val state = rememberTimePickerState(initialHour = initial.hour, initialMinute = initial.minute, is24Hour = true)
    AlertDialog(onDismissRequest = onDismiss, containerColor = MaterialTheme.colorScheme.surface, tonalElevation = 0.dp,
        title = { Text(stringResource(R.string.arrival_time)) },
        text = { Box(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState())) { TimeInput(state, colors = TimePickerDefaults.colors(
            timeSelectorUnselectedContainerColor = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.04f))) } },
        confirmButton = { TextButton(onClick = { onSelect(LocalTime.of(state.hour, state.minute).format(DateTimeFormatter.ofPattern("HH:mm"))) }) {
            Text(stringResource(android.R.string.ok))
        } }, dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(android.R.string.cancel)) } })
}
