package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
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
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.AbsoluteAlignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.nags.operations.R
import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.ui.components.CustomerLogo
import com.nags.operations.ui.util.formatIsoDateForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import com.nags.operations.ui.util.parseInUserZone
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch

private val ArrivalFormGround = Color(0xFFF2F2F7)

/** Temporary entry only; actor attribution and saving belong to the future submission flow. */
@Composable
internal fun FlightArrivalForm(
    flight: FlightDetail,
    aircraftTypes: List<AircraftTypeCatalogItem>,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onClose: () -> Unit,
) {
    val initialAircraftId = rememberSaveable { flight.aircraftTypeId.orEmpty() }
    val initialAircraftModel = rememberSaveable { flight.aircraftTypeModel.orEmpty() }
    val initialAta = rememberSaveable {
        runCatching { parseInUserZone(flight.scheduledArrivalUtc).toLocalDateTime().withSecond(0).withNano(0).toString() }.getOrDefault("")
    }
    val referenceYear = rememberSaveable { LocalDate.now().year }
    var aircraftId by rememberSaveable { mutableStateOf(initialAircraftId) }
    var ata by rememberSaveable { mutableStateOf(initialAta) }
    var tailNumber by rememberSaveable { mutableStateOf("") }
    var reference by rememberSaveable { mutableStateOf("") }
    var remarks by rememberSaveable { mutableStateOf("") }
    var picker by rememberSaveable { mutableStateOf<String?>(null) }
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    val hasChanges = aircraftId != initialAircraftId || ata != initialAta || listOf(tailNumber, reference, remarks).any { it.isNotEmpty() }
    val requestClose = { if (hasChanges) confirmDiscard = true else onClose() }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val title = stringResource(R.string.arrival_title)
    val unavailable = stringResource(R.string.flights_card_aircraft_unavailable)
    val scheduled = stringResource(R.string.flights_scheduled)
    val aircraftChoices = remember(aircraftTypes, initialAircraftId, initialAircraftModel, unavailable, scheduled) {
        val choices = aircraftTypes.map { ArrivalChoice(it.id, it.model, it.manufacturer) }.toMutableList()
        if (initialAircraftId.isNotBlank() && choices.none { it.id == initialAircraftId }) {
            choices += ArrivalChoice(initialAircraftId, initialAircraftModel.ifBlank { unavailable }, scheduled)
        }
        choices.sortedBy { it.title }
    }
    val selectedAircraft = aircraftChoices.firstOrNull { it.id == aircraftId }?.title.orEmpty()
    fun openPicker(kind: String) {
        focus.clearFocus(); keyboard?.hide()
        if (kind == "ata" && ata.isBlank()) ata = LocalDateTime.now().withSecond(0).withNano(0).toString()
        picker = kind
    }
    Dialog(onDismissRequest = { if (picker != null) picker = null else requestClose() },
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        Surface(Modifier.fillMaxSize(), color = ArrivalFormGround) {
            Column(Modifier.fillMaxSize().safeDrawingPadding().imePadding().semantics { paneTitle = title },
                horizontalAlignment = Alignment.CenterHorizontally) {
                Row(Modifier.widthIn(max = 640.dp).fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    IconButton(onClick = { focus.clearFocus(); keyboard?.hide(); requestClose() }, modifier = Modifier.size(48.dp)) {
                        Box(Modifier.size(34.dp).background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.05f), CircleShape), contentAlignment = Alignment.Center) {
                            Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel), Modifier.size(20.dp))
                        }
                    }
                    Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold,
                        textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    IconButton(onClick = {}, enabled = false, modifier = Modifier.size(48.dp)) {
                        Box(Modifier.size(34.dp).background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.12f), CircleShape), contentAlignment = Alignment.Center) {
                            Icon(Icons.Filled.Check, stringResource(R.string.arrival_save), Modifier.size(20.dp),
                                tint = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.35f))
                        }
                    }
                }
                Column(Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState()), horizontalAlignment = Alignment.CenterHorizontally) {
                    Column(Modifier.widthIn(max = 600.dp).fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp),
                        verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        ArrivalFlightIdentity(flight, loadCustomerLogo)
                        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            ContactFieldGroup { AtaField(ata, onClick = { openPicker("ata") }) }
                            Text(stringResource(R.string.arrival_local_time, ZoneId.systemDefault().id), Modifier.padding(horizontal = 14.dp),
                                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        ContactFieldGroup {
                            ContactChoiceRow(stringResource(R.string.arrival_aircraft), selectedAircraft.ifBlank { stringResource(R.string.arrival_select_aircraft) },
                                stringResource(R.string.arrival_aircraft_type), isPlaceholder = selectedAircraft.isBlank(), onClick = { openPicker("aircraft") })
                            ContactRowDivider()
                            ContactTextRow(stringResource(R.string.arrival_tail_number), tailNumber, { tailNumber = it },
                                stringResource(R.string.arrival_tail_hint), keyboardOptions = KeyboardOptions(
                                    capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next))
                        }
                        ContactFieldGroup {
                            ContactTextRow(stringResource(R.string.arrival_reference), reference, { reference = it }, stringResource(R.string.arrival_optional))
                        }
                        ContactFieldGroup {
                            Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                Text(stringResource(R.string.arrival_remarks), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold)
                                ContactTextInput(remarks, { remarks = it }, stringResource(R.string.arrival_remarks_hint),
                                    stringResource(R.string.arrival_remarks), multiline = true, textAlign = TextAlign.Left, keyboardOptions = KeyboardOptions(
                                        capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done))
                            }
                        }
                    }
                }
            }
        }
    }
    if (picker == "ata") ArrivalInputSheet(title = stringResource(R.string.arrival_ata), onDismiss = { picker = null }) { _ ->
        Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp), contentAlignment = Alignment.Center) {
            ArrivalWheelPicker(LocalDateTime.parse(ata), referenceYear, onValueChange = { ata = it.toString() })
        }
    }
    if (picker == "aircraft") ArrivalAircraftPicker(title = stringResource(R.string.arrival_aircraft_type),
        options = aircraftChoices, selectedId = aircraftId, onDismiss = { picker = null }, onSelect = { aircraftId = it })
    if (confirmDiscard) AlertDialog(onDismissRequest = { confirmDiscard = false }, containerColor = MaterialTheme.colorScheme.surface, tonalElevation = 0.dp,
        title = { Text(stringResource(R.string.arrival_discard_title)) }, text = { Text(stringResource(R.string.arrival_discard_hint)) },
        confirmButton = { TextButton(onClick = onClose) { Text(stringResource(R.string.arrival_discard), color = MaterialTheme.colorScheme.error) } },
        dismissButton = { TextButton(onClick = { confirmDiscard = false }) { Text(stringResource(R.string.arrival_keep_editing)) } })
}

/** Keep the three compact text lines level with the logo; the status stays at the right edge. */
@Composable
private fun ArrivalFlightIdentity(flight: FlightDetail, loadCustomerLogo: (String) -> Flow<ByteArray?>) {
    val logoSize = with(LocalDensity.current) {
        MaterialTheme.typography.titleSmall.lineHeight.toDp() + MaterialTheme.typography.bodySmall.lineHeight.toDp() * 2
    }
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp, vertical = 4.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
            CustomerLogo(flight.customerId, loadCustomerLogo, Modifier.size(logoSize))
            Column(Modifier.weight(1f)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    Text(flight.flightNumber, Modifier.weight(1f), style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Surface(Modifier.widthIn(max = 88.dp * LocalDensity.current.fontScale), shape = RoundedCornerShape(50),
                        color = MaterialTheme.colorScheme.tertiary.copy(alpha = 0.11f)) {
                        Text(stringResource(R.string.flights_scheduled), Modifier.padding(horizontal = 8.dp, vertical = 2.dp),
                            style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.SemiBold,
                            color = MaterialTheme.colorScheme.tertiary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
                Text(flight.customerName, style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(stringResource(R.string.arrival_context_schedule, flight.stationIata,
                    "${formatIsoDateForDisplay(flight.scheduledArrivalUtc)} ${formatIsoTimeForDisplay(flight.scheduledArrivalUtc)}"),
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun AtaField(value: String, onClick: () -> Unit) {
    val locale = LocalLocale.current.platformLocale
    val fontScale = LocalDensity.current.fontScale
    val ataDescription = stringResource(R.string.arrival_choose_ata)
    val dateTime = value.takeIf { it.isNotBlank() }?.let(LocalDateTime::parse)
    Surface(onClick = onClick, color = MaterialTheme.colorScheme.surface,
        modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).semantics { contentDescription = ataDescription }) {
        BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 6.dp), contentAlignment = Alignment.Center) {
            val compact = maxWidth < 280.dp * fontScale
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.arrival_ata_short), Modifier.widthIn(min = 40.dp),
                    style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold)
                val format = if (compact) "MMM d, yyyy\nHH:mm" else "MMM d, yyyy · HH:mm"
                Text(dateTime?.format(DateTimeFormatter.ofPattern(format, locale)) ?: "—", Modifier.weight(1f),
                    style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Medium, textAlign = TextAlign.Right,
                    maxLines = if (compact) 2 else 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun ContactFieldGroup(content: @Composable () -> Unit) {
    // Keep labels on the left and entered values on the physical right in both locales.
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surface)) { content() }
    }
}

@Composable
private fun ContactRowDivider() {
    HorizontalDivider(Modifier.padding(start = 14.dp), thickness = 0.5.dp, color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.6f))
}

@Composable
private fun ContactChoiceRow(label: String, value: String, description: String, isPlaceholder: Boolean = false, onClick: () -> Unit) {
    Surface(onClick = onClick, color = MaterialTheme.colorScheme.surface,
        modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).semantics { contentDescription = description }) {
        Row(Modifier.padding(horizontal = 14.dp, vertical = 6.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(label, Modifier.weight(0.32f), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
                maxLines = 2, overflow = TextOverflow.Ellipsis)
            Row(Modifier.weight(0.68f), horizontalArrangement = Arrangement.End, verticalAlignment = Alignment.CenterVertically) {
                Text(value, Modifier.weight(1f, fill = false), style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.Right,
                    fontWeight = if (isPlaceholder) FontWeight.Normal else FontWeight.Medium,
                    fontStyle = if (isPlaceholder) FontStyle.Italic else FontStyle.Normal,
                    color = if (isPlaceholder) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, Modifier.padding(start = 4.dp).size(18.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
private fun ContactTextRow(label: String, value: String, onChange: (String) -> Unit, placeholder: String,
    keyboardOptions: KeyboardOptions = KeyboardOptions(imeAction = ImeAction.Next)) {
    Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).padding(horizontal = 14.dp),
        horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, Modifier.weight(0.32f), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
            maxLines = 2, overflow = TextOverflow.Ellipsis)
        ContactTextInput(value, onChange, placeholder, label, Modifier.weight(0.68f), keyboardOptions = keyboardOptions)
    }
}

@Composable
private fun ContactTextInput(value: String, onChange: (String) -> Unit, placeholder: String, label: String,
    modifier: Modifier = Modifier, multiline: Boolean = false, textAlign: TextAlign = TextAlign.Right,
    keyboardOptions: KeyboardOptions = KeyboardOptions(imeAction = ImeAction.Next)) {
    val contentAlignment = if (textAlign == TextAlign.Left) {
        if (multiline) AbsoluteAlignment.TopLeft else AbsoluteAlignment.CenterLeft
    } else {
        if (multiline) AbsoluteAlignment.TopRight else AbsoluteAlignment.CenterRight
    }
    BasicTextField(value, onChange, modifier = modifier.fillMaxWidth().heightIn(min = 48.dp)
        .semantics { contentDescription = label },
        textStyle = MaterialTheme.typography.bodyMedium.copy(color = MaterialTheme.colorScheme.onSurface, textAlign = textAlign, fontWeight = FontWeight.Medium),
        cursorBrush = SolidColor(MaterialTheme.colorScheme.primary), singleLine = !multiline,
        minLines = if (multiline) 2 else 1, maxLines = if (multiline) 5 else 1, keyboardOptions = keyboardOptions,
        decorationBox = { inner -> Box(Modifier.fillMaxWidth(), contentAlignment = contentAlignment) {
            if (value.isEmpty()) Text(placeholder, Modifier.fillMaxWidth(), style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic, textAlign = textAlign, color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = if (multiline) Int.MAX_VALUE else 1, overflow = TextOverflow.Ellipsis)
            inner()
        } })
}

/** Both input pickers use the same rounded, dimming sheet behavior as flight details. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ArrivalInputSheet(title: String, onDismiss: () -> Unit, content: @Composable (close: () -> Unit) -> Unit) {
    val state = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val scope = rememberCoroutineScope()
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = state,
        shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp), containerColor = MaterialTheme.colorScheme.surface,
        tonalElevation = 0.dp, scrimColor = Color.Black.copy(alpha = 0.28f),
        dragHandle = { Box(Modifier.padding(top = 8.dp, bottom = 4.dp).size(width = 32.dp, height = 4.dp)
            .background(MaterialTheme.colorScheme.outlineVariant, CircleShape)) }) {
        Column(Modifier.fillMaxWidth().semantics { paneTitle = title }) {
            content { scope.launch { state.hide(); onDismiss() } }
        }
    }
}

private data class ArrivalChoice(val id: String, val title: String, val subtitle: String)

@Composable
private fun ArrivalAircraftPicker(title: String, options: List<ArrivalChoice>, selectedId: String,
    onDismiss: () -> Unit, onSelect: (String) -> Unit) {
    var search by rememberSaveable { mutableStateOf("") }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val searchDescription = stringResource(R.string.arrival_search_aircraft)
    val filtered = remember(options, search) {
        options.filter { it.title.contains(search.trim(), ignoreCase = true) || it.subtitle.contains(search.trim(), ignoreCase = true) }
    }
    ArrivalInputSheet(title, onDismiss) { close ->
        Column(Modifier.fillMaxWidth().heightIn(max = 600.dp).fillMaxHeight(0.85f)) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.size(48.dp))
                Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold,
                    textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
                IconButton(onClick = close, modifier = Modifier.size(48.dp)) {
                    Box(Modifier.size(30.dp).background(ArrivalFormGround, CircleShape), contentAlignment = Alignment.Center) {
                        Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel), Modifier.size(18.dp))
                    }
                }
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp).clip(RoundedCornerShape(12.dp))
                .background(ArrivalFormGround), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.Search, null, Modifier.padding(start = 12.dp, end = 8.dp).size(20.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                BasicTextField(search, { search = it }, modifier = Modifier.weight(1f).heightIn(min = 48.dp)
                    .semantics { contentDescription = searchDescription }, singleLine = true,
                    textStyle = MaterialTheme.typography.bodyLarge.copy(color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Medium),
                    cursorBrush = SolidColor(MaterialTheme.colorScheme.primary), keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { focus.clearFocus(); keyboard?.hide() }),
                    decorationBox = { inner -> Box(contentAlignment = Alignment.CenterStart) {
                        if (search.isEmpty()) Text(stringResource(R.string.arrival_search_aircraft), style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic,
                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        inner()
                    } })
                if (search.isNotEmpty()) IconButton(onClick = { search = "" }, modifier = Modifier.size(48.dp)) {
                    Icon(Icons.Outlined.Close, stringResource(R.string.flights_search_clear), Modifier.size(18.dp))
                } else Spacer(Modifier.size(12.dp))
            }
            if (filtered.isEmpty()) Text(if (options.isEmpty()) stringResource(R.string.arrival_no_aircraft) else stringResource(R.string.arrival_no_results),
                Modifier.padding(20.dp), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            else LazyColumn(Modifier.weight(1f).fillMaxWidth()) {
                itemsIndexed(filtered, key = { _, option -> option.id }) { index, option ->
                    val selected = option.id == selectedId
                    Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).selectable(selected = selected, role = Role.RadioButton,
                        onClick = { onSelect(option.id); focus.clearFocus(); keyboard?.hide(); close() })
                        .padding(horizontal = 20.dp, vertical = 10.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(option.title, style = MaterialTheme.typography.bodyLarge,
                                fontWeight = if (selected) FontWeight.Bold else FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                            if (option.subtitle.isNotBlank()) Text(option.subtitle, style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                        if (selected) Icon(Icons.Filled.Check, stringResource(R.string.arrival_aircraft_selected), Modifier.size(20.dp), tint = MaterialTheme.colorScheme.primary)
                        else Spacer(Modifier.size(20.dp))
                    }
                    if (index < filtered.lastIndex) HorizontalDivider(Modifier.padding(start = 20.dp), thickness = 0.5.dp,
                        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.6f))
                }
            }
        }
    }
}
