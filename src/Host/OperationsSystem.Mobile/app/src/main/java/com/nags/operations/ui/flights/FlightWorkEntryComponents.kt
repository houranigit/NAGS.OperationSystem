package com.nags.operations.ui.flights

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AddCircle
import androidx.compose.material.icons.filled.RemoveCircle
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import java.time.ZoneId
import com.nags.operations.R
import java.time.LocalDateTime

private val WorkAddGreen = Color(0xFF248A3D)
private val WorkRemoveRed = Color(0xFFD92D3F)

/** The same quiet physical-left rule binds each service or task's full-width fields. */
@Composable
internal fun FlightWorkEntryGroup(content: @Composable () -> Unit) {
    val border = MaterialTheme.colorScheme.primary.copy(alpha = 0.32f)
    Box(Modifier.fillMaxWidth().drawWithContent {
        drawContent()
        val stroke = 0.75.dp.toPx()
        val inset = 14.dp.toPx()
        drawLine(border, Offset(stroke / 2, inset), Offset(stroke / 2, size.height - inset),
            strokeWidth = stroke, cap = StrokeCap.Round)
    }) {
        FlightFormFieldGroup(content)
    }
}

/** Intrinsic labels leave the remaining row width to the selected value. */
@Composable
internal fun FlightWorkChoiceRow(label: String, value: String, description: String,
    isPlaceholder: Boolean = false, leadingPadding: Dp = 14.dp, onClick: () -> Unit) {
    FlightFormChoiceRow(label, value, description, isPlaceholder, leadingPadding, onClick = onClick)
}

@Composable
internal fun FlightWorkAddRow(label: String, onClick: () -> Unit) {
    Surface(onClick = onClick, color = MaterialTheme.colorScheme.surface,
        modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(48.dp), contentAlignment = Alignment.Center) {
                Icon(Icons.Filled.AddCircle, null, Modifier.size(20.dp), tint = WorkAddGreen)
            }
            Text(label, Modifier.weight(1f).padding(end = 14.dp), style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Medium, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
    }
}

/** The minus reveals Delete first, keeping temporary removal deliberate and reversible. */
@Composable
internal fun FlightWorkRemovableEntry(
    id: String,
    removeDescription: String,
    onRemove: () -> Unit,
    compactDelete: Boolean = false,
    content: @Composable ColumnScope.() -> Unit,
) {
    var removeOpen by rememberSaveable(id) { mutableStateOf(false) }
    val shift by animateDpAsState(if (removeOpen) (-80).dp else 0.dp, animationSpec = tween(180), label = "workDeleteReveal")
    Row(Modifier.fillMaxWidth().animateContentSize(), verticalAlignment = Alignment.Top) {
        IconButton(onClick = { removeOpen = !removeOpen }, modifier = Modifier.size(48.dp)) {
            Icon(Icons.Filled.RemoveCircle, removeDescription, Modifier.size(20.dp), tint = WorkRemoveRed)
        }
        Box(Modifier.weight(1f).clipToBounds()) {
            if (removeOpen) Box(Modifier.align(Alignment.TopEnd).width(80.dp).heightIn(min = 48.dp), contentAlignment = Alignment.TopEnd) {
                Button(onClick = onRemove,
                    modifier = if (compactDelete) Modifier.align(Alignment.Center).width(64.dp).height(34.dp)
                        else Modifier.width(80.dp).heightIn(min = 48.dp),
                    shape = RoundedCornerShape(if (compactDelete) 10.dp else 12.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = WorkRemoveRed, contentColor = Color.White),
                    contentPadding = androidx.compose.foundation.layout.PaddingValues(
                        horizontal = if (compactDelete) 6.dp else 10.dp, vertical = if (compactDelete) 0.dp else 8.dp)) {
                    Text(stringResource(R.string.work_delete),
                        style = if (compactDelete) MaterialTheme.typography.bodySmall else MaterialTheme.typography.bodyMedium,
                        fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            Column(Modifier.fillMaxWidth().offset { IntOffset(shift.roundToPx(), 0) }.background(MaterialTheme.colorScheme.surface), content = content)
        }
    }
}

@Composable
internal fun FlightWorkPeriodFields(
    period: FlightWorkPeriod,
    referenceYear: Int,
    zone: ZoneId,
    onChange: (FlightWorkPeriod) -> Unit,
) {
    var picker by rememberSaveable { mutableStateOf<String?>(null) }
    val startLabel = stringResource(R.string.work_start)
    val endLabel = stringResource(R.string.work_end)
    FlightFormDateTimeField(period.start, startLabel, startLabel, onClick = { picker = "start" })
    FlightFormDivider()
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.weight(1f)) {
            if (period.end.isBlank()) FlightFormChoiceRow(endLabel, stringResource(R.string.work_add_end), endLabel,
                isPlaceholder = true, onClick = { onChange(period.copy(end = period.start)); picker = "end" })
            else FlightFormDateTimeField(period.end, endLabel, endLabel, onClick = { picker = "end" })
        }
        if (period.end.isNotBlank()) IconButton(onClick = { onChange(period.copy(end = "")) }, modifier = Modifier.size(48.dp)) {
            Icon(Icons.Outlined.Close, stringResource(R.string.work_clear_end), Modifier.size(18.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
    val endBeforeStart = runCatching {
        period.end.isNotBlank() && LocalDateTime.parse(period.end).isBefore(LocalDateTime.parse(period.start))
    }.getOrDefault(false)
    if (endBeforeStart) Text(stringResource(R.string.work_end_before_start),
        Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 6.dp).semantics { liveRegion = LiveRegionMode.Polite },
        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
    if (picker != null) {
        val editingEnd = picker == "end"
        val value = runCatching { LocalDateTime.parse(if (editingEnd) period.end.ifBlank { period.start } else period.start) }
            .getOrElse { LocalDateTime.now(zone).withSecond(0).withNano(0) }
        FlightFormDateTimeSheet(value, referenceYear,
            onValueChange = { if (editingEnd) onChange(period.copy(end = it.toString())) else onChange(period.copy(start = it.toString())) },
            onDismiss = { picker = null }, title = if (editingEnd) endLabel else startLabel)
    }
}

@Composable
internal fun FlightWorkNotes(
    reference: String,
    onReferenceChange: (String) -> Unit,
    remarks: String,
    onRemarksChange: (String) -> Unit,
    description: String,
    showReference: Boolean = true,
) {
    if (showReference) {
        FlightFormDivider()
        FlightWorkReferenceRow(reference, onReferenceChange)
    }
    FlightFormDivider()
    Column(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(stringResource(R.string.flight_form_remarks), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold)
        FlightFormTextInput(remarks, onRemarksChange, stringResource(R.string.flight_form_remarks_hint), description,
            multiline = true, textAlign = TextAlign.Left, keyboardOptions = KeyboardOptions(
                capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done))
    }
}

@Composable
internal fun FlightWorkReferenceRow(reference: String, onChange: (String) -> Unit) {
    FlightFormTextRow(stringResource(R.string.work_customer_reference), reference, onChange,
        stringResource(R.string.arrival_optional))
}
