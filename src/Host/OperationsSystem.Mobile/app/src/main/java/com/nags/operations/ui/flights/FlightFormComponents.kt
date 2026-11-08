package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.TextAutoSize
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.outlined.Close
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
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.AbsoluteAlignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInWindow
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
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
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.ui.components.CustomerLogo
import com.nags.operations.ui.util.formatIsoDateForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch

internal val FlightFormGround = Color(0xFFF2F2F7)

/** Shared entry chrome; saving deliberately has no callback or persistence dependency. */
@Composable
internal fun FlightRecordingFormScaffold(
    flight: FlightDetail,
    title: String,
    saveDescription: String,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onRequestClose: () -> Unit,
    content: @Composable ColumnScope.() -> Unit,
) {
    Dialog(onDismissRequest = onRequestClose,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        val focus = LocalFocusManager.current
        val keyboard = LocalSoftwareKeyboardController.current
        val editor = remember { FlightFormFocusedEditor() }
        CompositionLocalProvider(LocalFlightFormFocusedEditor provides editor) {
            Surface(Modifier.fillMaxSize().onGloballyPositioned { editor.origin = it.positionInWindow() }
                .pointerInput(editor, focus, keyboard) {
                    // Observe the release after child controls process it, without consuming their input.
                    awaitEachGesture {
                        val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                        var isTap = true
                        var release = down.position
                        do {
                            val event = awaitPointerEvent(PointerEventPass.Final)
                            if (event.changes.size > 1 || event.changes.any {
                                (it.position - down.position).getDistance() > viewConfiguration.touchSlop
                            }) isTap = false
                            event.changes.firstOrNull { it.id == down.id }?.let { release = it.position }
                        } while (event.changes.any { it.pressed })
                        val bounds = editor.bounds
                        if (isTap && bounds != null && !bounds.contains(release + editor.origin)) {
                            focus.clearFocus()
                            keyboard?.hide()
                        }
                    }
                }, color = FlightFormGround) {
                Column(Modifier.fillMaxSize().safeDrawingPadding().imePadding().semantics { paneTitle = title },
                    horizontalAlignment = Alignment.CenterHorizontally) {
                    Row(Modifier.widthIn(max = 640.dp).fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically) {
                        IconButton(onClick = { focus.clearFocus(); keyboard?.hide(); onRequestClose() }, modifier = Modifier.size(48.dp)) {
                            Box(Modifier.size(34.dp).background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.05f), CircleShape), contentAlignment = Alignment.Center) {
                                Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel), Modifier.size(20.dp))
                            }
                        }
                        Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold,
                            textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        IconButton(onClick = {}, enabled = false, modifier = Modifier.size(48.dp)) {
                            Box(Modifier.size(34.dp).background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.12f), CircleShape), contentAlignment = Alignment.Center) {
                                Icon(Icons.Filled.Check, saveDescription, Modifier.size(20.dp),
                                    tint = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.35f))
                            }
                        }
                    }
                    Column(Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState()), horizontalAlignment = Alignment.CenterHorizontally) {
                        Column(Modifier.widthIn(max = 600.dp).fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp),
                            verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            FlightRecordingIdentity(flight, loadCustomerLogo)
                            content()
                        }
                    }
                }
            }
        }
    }
}

/** Keep the three compact text lines level with the logo; the status stays at the right edge. */
@Composable
private fun FlightRecordingIdentity(flight: FlightDetail, loadCustomerLogo: (String) -> Flow<ByteArray?>) {
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
                Text(stringResource(R.string.flight_form_context_schedule, flight.stationIata,
                    "${formatIsoDateForDisplay(flight.scheduledArrivalUtc)} ${formatIsoTimeForDisplay(flight.scheduledArrivalUtc)}"),
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
internal fun FlightFormDateTimeField(value: String, label: String, description: String, onClick: () -> Unit) {
    val locale = LocalLocale.current.platformLocale
    val dateTime = value.takeIf { it.isNotBlank() }?.let(LocalDateTime::parse)
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    Surface(onClick = { focus.clearFocus(); keyboard?.hide(); onClick() }, color = MaterialTheme.colorScheme.surface,
        modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).semantics { contentDescription = description }) {
        BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 6.dp), contentAlignment = Alignment.Center) {
            val labelMaxWidth = maxWidth * 0.45f
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(label, Modifier.widthIn(max = labelMaxWidth),
                    style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                FlightFormValueText(dateTime?.format(DateTimeFormatter.ofPattern("MMM d, yyyy · HH:mm", locale)) ?: "—",
                    Modifier.weight(1f))
            }
        }
    }
}

/** A small type adjustment precedes ellipsis; system font scaling still applies. */
@Composable
internal fun FlightFormValueText(value: String, modifier: Modifier = Modifier, isPlaceholder: Boolean = false) {
    val style = MaterialTheme.typography.bodyMedium
    Text(value, modifier, style = style, textAlign = TextAlign.Right,
        autoSize = TextAutoSize.StepBased(minFontSize = style.fontSize * 0.9f, maxFontSize = style.fontSize, stepSize = 0.5.sp),
        fontWeight = if (isPlaceholder) FontWeight.Normal else FontWeight.Medium,
        fontStyle = if (isPlaceholder) FontStyle.Italic else FontStyle.Normal,
        color = if (isPlaceholder) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
        maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
}

@Composable
internal fun FlightFormFieldGroup(content: @Composable () -> Unit) {
    // Keep labels on the left and entered values on the physical right in both locales.
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surface)) { content() }
    }
}

@Composable
internal fun FlightFormDivider() {
    HorizontalDivider(Modifier.padding(start = 14.dp), thickness = 0.5.dp, color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.6f))
}

@Composable
internal fun FlightFormChoiceRow(label: String, value: String, description: String, isPlaceholder: Boolean = false,
    leadingPadding: Dp = 14.dp, onClick: () -> Unit) {
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    Surface(onClick = { focus.clearFocus(); keyboard?.hide(); onClick() }, color = MaterialTheme.colorScheme.surface,
        modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).semantics { contentDescription = description }) {
        BoxWithConstraints(Modifier.fillMaxWidth().padding(start = leadingPadding, end = 14.dp, top = 6.dp, bottom = 6.dp), contentAlignment = Alignment.Center) {
            val labelMaxWidth = maxWidth * 0.45f
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(label, Modifier.widthIn(max = labelMaxWidth), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                FlightFormValueText(value, Modifier.weight(1f), isPlaceholder)
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, Modifier.size(18.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
internal fun FlightFormTextRow(label: String, value: String, onChange: (String) -> Unit, placeholder: String,
    keyboardOptions: KeyboardOptions = KeyboardOptions(imeAction = ImeAction.Next)) {
    BoxWithConstraints(Modifier.fillMaxWidth().heightIn(min = 48.dp).padding(horizontal = 14.dp)) {
        val labelMaxWidth = maxWidth * 0.45f
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(label, Modifier.widthIn(max = labelMaxWidth), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
                maxLines = 1, overflow = TextOverflow.Ellipsis)
            FlightFormTextInput(value, onChange, placeholder, label, Modifier.weight(1f), keyboardOptions = keyboardOptions)
        }
    }
}

@Composable
internal fun FlightFormTextInput(value: String, onChange: (String) -> Unit, placeholder: String, label: String,
    modifier: Modifier = Modifier, multiline: Boolean = false, textAlign: TextAlign = TextAlign.Right,
    keyboardOptions: KeyboardOptions = KeyboardOptions(imeAction = ImeAction.Next)) {
    val contentAlignment = if (textAlign == TextAlign.Left) {
        if (multiline) AbsoluteAlignment.TopLeft else AbsoluteAlignment.CenterLeft
    } else {
        if (multiline) AbsoluteAlignment.TopRight else AbsoluteAlignment.CenterRight
    }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    BasicTextField(value, onChange, modifier = modifier.fillMaxWidth().heightIn(min = 48.dp).flightFormEditableFocus()
        .semantics { contentDescription = label },
        textStyle = MaterialTheme.typography.bodyMedium.copy(color = MaterialTheme.colorScheme.onSurface, textAlign = textAlign, fontWeight = FontWeight.Medium),
        cursorBrush = SolidColor(MaterialTheme.colorScheme.primary), singleLine = !multiline,
        minLines = if (multiline) 2 else 1, maxLines = if (multiline) 5 else 1, keyboardOptions = keyboardOptions,
        keyboardActions = KeyboardActions(onDone = { focus.clearFocus(); keyboard?.hide() }),
        decorationBox = { inner -> Box(Modifier.fillMaxWidth(), contentAlignment = contentAlignment) {
            if (value.isEmpty()) Text(placeholder, Modifier.fillMaxWidth(), style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic, textAlign = textAlign, color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = if (multiline) Int.MAX_VALUE else 1, overflow = TextOverflow.Ellipsis)
            inner()
        } })
}

/** Tracks the current editor, so outside taps never steal a newly transferred focus. */
private class FlightFormFocusedEditor {
    var owner: Any? = null
    var bounds: Rect? = null
    var origin = Offset.Zero
    fun clear(owner: Any) { if (this.owner === owner) { this.owner = null; bounds = null } }
}

private class FlightFormEditorPosition {
    var focused = false
    var bounds = Rect.Zero
}

private val LocalFlightFormFocusedEditor = staticCompositionLocalOf<FlightFormFocusedEditor?> { null }

@Composable
internal fun Modifier.flightFormEditableFocus(): Modifier {
    val editor = LocalFlightFormFocusedEditor.current ?: return this
    val owner = remember { Any() }
    val position = remember { FlightFormEditorPosition() }
    DisposableEffect(editor, owner) { onDispose { editor.clear(owner) } }
    return onGloballyPositioned {
        position.bounds = it.boundsInWindow()
        if (position.focused) { editor.owner = owner; editor.bounds = position.bounds }
    }.onFocusChanged {
        position.focused = it.hasFocus
        if (it.hasFocus) { editor.owner = owner; editor.bounds = position.bounds }
        else editor.clear(owner)
    }
}

/** Both input pickers use the same rounded, dimming sheet behavior as flight details. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun FlightFormModalSheet(title: String, onDismiss: () -> Unit, content: @Composable (close: () -> Unit) -> Unit) {
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


@Composable
internal fun FlightFormTimeZoneHint() {
    Text(stringResource(R.string.flight_form_local_time, ZoneId.systemDefault().id), Modifier.padding(horizontal = 14.dp),
        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

@Composable
internal fun FlightFormRemarks(value: String, onValueChange: (String) -> Unit, description: String) {
    FlightFormFieldGroup {
        Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(stringResource(R.string.flight_form_remarks), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold)
            FlightFormTextInput(value, onValueChange, stringResource(R.string.flight_form_remarks_hint), description,
                multiline = true, textAlign = TextAlign.Left, keyboardOptions = KeyboardOptions(
                    capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done))
        }
    }
}

@Composable
internal fun FlightFormDateTimeSheet(value: LocalDateTime, referenceYear: Int, onValueChange: (LocalDateTime) -> Unit,
    onDismiss: () -> Unit, title: String = stringResource(R.string.flight_form_date_time)) {
    FlightFormModalSheet(title, onDismiss) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp), contentAlignment = Alignment.Center) {
            ArrivalWheelPicker(value, referenceYear, onValueChange)
        }
    }
}

@Composable
internal fun FlightFormDiscardDialog(title: String, onDismiss: () -> Unit, onDiscard: () -> Unit) {
    AlertDialog(onDismissRequest = onDismiss, containerColor = MaterialTheme.colorScheme.surface, tonalElevation = 0.dp,
        title = { Text(title) }, text = { Text(stringResource(R.string.flight_form_discard_hint)) },
        confirmButton = { TextButton(onClick = onDiscard) { Text(stringResource(R.string.flight_form_discard), color = MaterialTheme.colorScheme.error) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.flight_form_keep_editing)) } })
}
