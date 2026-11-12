package com.nags.operations.ui.flights

import android.os.Build
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.border
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
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
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
import androidx.compose.material.icons.outlined.Flight
import androidx.compose.material.icons.outlined.Sync
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
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.AbsoluteAlignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.MotionDurationScale
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.BlurEffect
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.layer.drawLayer
import androidx.compose.ui.graphics.rememberGraphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInWindow
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.error
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.text
import androidx.compose.ui.text.AnnotatedString
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
import androidx.compose.ui.window.DialogWindowProvider
import androidx.core.view.WindowCompat
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.ui.components.CustomerLogo
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch

internal val FlightFormGround = Color(0xFFF2F2F7)
internal val FlightFormGreen = Color(0xFF34C759)

/** Used only while work is in progress; honor the system animation setting. */
@Composable
internal fun Modifier.flightFormRotating(active: Boolean): Modifier {
    val motionScale = rememberCoroutineScope().coroutineContext[MotionDurationScale]
    if (!active || motionScale?.scaleFactor == 0f) return this
    val rotation = rememberInfiniteTransition(label = "Flight form progress").animateFloat(
        initialValue = 0f, targetValue = 360f,
        animationSpec = infiniteRepeatable(tween(1000, easing = LinearEasing)), label = "Progress rotation")
    return graphicsLayer { rotationZ = rotation.value }
}

/** Shared entry chrome; each recording form owns its save behavior. */
@Composable
internal fun FlightRecordingFormScaffold(
    flight: FlightDetail,
    title: String,
    saveDescription: String,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onRequestClose: () -> Unit,
    saveEnabled: Boolean = false,
    saveLoading: Boolean = false,
    onSave: () -> Unit = {},
    identityDetails: @Composable () -> Unit = {},
    progressStatus: FlightProgressStatus? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    Dialog(onDismissRequest = onRequestClose,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        val view = LocalView.current
        val window = (view.parent as? DialogWindowProvider)?.window
        SideEffect {
            // Let the window own both directions, so confirmed close keeps its native exit animation.
            // The dedicated dialog window is disposed with this form; no activity window is modified.
            window?.setWindowAnimations(R.style.Animation_OperationsFlightForm)
            window?.setDimAmount(0.28f)
            window?.let {
                WindowCompat.getInsetsController(it, view).isAppearanceLightStatusBars = true
            }
        }
        val focus = LocalFocusManager.current
        val keyboard = LocalSoftwareKeyboardController.current
        val editor = remember { FlightFormFocusedEditor() }
        val scroll = rememberScrollState()
        val backdrop = rememberGraphicsLayer()
        val density = LocalDensity.current
        var headerHeight by remember { mutableIntStateOf(with(density) { 56.dp.roundToPx() }) }
        var viewportTop by remember { mutableFloatStateOf(0f) }
        var titleTop by remember(flight.id) { mutableFloatStateOf(Float.MAX_VALUE) }
        var titleHeight by remember { mutableIntStateOf(1) }
        // Use the actual heading bounds so the handoff follows font scaling and rotation.
        val titleProgress by remember {
            derivedStateOf { ((scroll.value - titleTop + headerHeight) / titleHeight.coerceAtLeast(1)).coerceIn(0f, 1f) }
        }
        val supportsBlur = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
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
                }, color = FlightFormGround, shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp)) {
                Box(Modifier.fillMaxSize().imePadding().semantics { paneTitle = title }) {
                    Column(Modifier.fillMaxSize().onGloballyPositioned { viewportTop = it.positionInWindow().y }
                        .drawWithContent {
                            if (supportsBlur) {
                                backdrop.record { this@drawWithContent.drawContent() }
                                drawLayer(backdrop)
                            } else drawContent()
                        }.background(FlightFormGround)
                        .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Horizontal + WindowInsetsSides.Bottom))
                        .verticalScroll(scroll), horizontalAlignment = Alignment.CenterHorizontally) {
                        Column(Modifier.widthIn(max = 600.dp).fillMaxWidth()
                            .padding(start = 16.dp, end = 16.dp, top = with(density) { headerHeight.toDp() } + 16.dp, bottom = 24.dp),
                            verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            FlightRecordingIdentity(flight, loadCustomerLogo, progressStatus ?: flight.progressStatus(), onTitlePositioned = { top, height ->
                                titleTop = top - viewportTop + scroll.value
                                titleHeight = height
                            }, details = identityDetails)
                            content()
                        }
                    }
                    Box(Modifier.fillMaxWidth().align(Alignment.TopCenter).onSizeChanged { headerHeight = it.height }.clipToBounds()) {
                        if (supportsBlur) Box(Modifier.matchParentSize().graphicsLayer {
                            renderEffect = BlurEffect(20.dp.toPx(), 20.dp.toPx())
                        }.drawBehind { drawLayer(backdrop) })
                        // The material intercepts touches to fields obscured underneath it.
                        Box(Modifier.matchParentSize().pointerInput(Unit) {}.drawBehind {
                            val materialProgress = (scroll.value / 24.dp.toPx()).coerceIn(0f, 1f)
                            val tint = if (supportsBlur) Color.White.copy(alpha = 0.55f * materialProgress)
                                else FlightFormGround.copy(alpha = 0.96f * materialProgress)
                            val edge = (1f - 10.dp.toPx() / size.height.coerceAtLeast(1f)).coerceIn(0f, 1f)
                            drawRect(Brush.verticalGradient(0f to tint, edge to tint, 1f to Color.Transparent))
                        })
                        Row(Modifier.windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal))
                            .widthIn(max = 640.dp).fillMaxWidth().align(Alignment.TopCenter).padding(horizontal = 12.dp, vertical = 4.dp),
                            verticalAlignment = Alignment.CenterVertically) {
                            FlightFormHeaderButton(Icons.Outlined.Close, stringResource(R.string.flights_cancel),
                                onClick = { focus.clearFocus(); keyboard?.hide(); onRequestClose() })
                            Text(flight.flightNumber, Modifier.weight(1f).graphicsLayer { alpha = titleProgress }
                                .clearAndSetSemantics { if (titleProgress >= 0.5f) text = AnnotatedString(flight.flightNumber) },
                                style = MaterialTheme.typography.titleMedium.copy(fontSize = 15.sp, lineHeight = 20.sp), fontWeight = FontWeight.SemiBold,
                                textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            FlightFormHeaderButton(if (saveLoading) Icons.Outlined.Sync else Icons.Filled.Check,
                                if (saveLoading) stringResource(R.string.arrival_saving) else saveDescription,
                                enabled = saveEnabled && !saveLoading, active = saveEnabled || saveLoading, loading = saveLoading,
                                onClick = { focus.clearFocus(); keyboard?.hide(); onSave() })
                        }
                    }
                }
            }
        }
    }
}

/** Compact circular controls with restrained elevation. */
@Composable
private fun FlightFormHeaderButton(icon: ImageVector, description: String, enabled: Boolean = true,
    active: Boolean = false, loading: Boolean = false, onClick: () -> Unit) {
    val face = if (active) FlightFormGreen
        else MaterialTheme.colorScheme.onSurface.copy(alpha = if (enabled) 0.05f else 0.08f)
    IconButton(onClick = onClick, enabled = enabled, modifier = Modifier.size(48.dp)) {
        Box(Modifier.size(34.dp)
            .shadow(1.5.dp, CircleShape, ambientColor = Color.Black.copy(alpha = 0.08f), spotColor = Color.Black.copy(alpha = 0.1f))
            .background(face, CircleShape),
            contentAlignment = Alignment.Center) {
            Icon(icon, description, Modifier.size(20.dp).flightFormRotating(loading),
                tint = if (active) Color.White else MaterialTheme.colorScheme.onSurface.copy(alpha = if (enabled) 1f else 0.35f))
        }
    }
}

/** Flight identity and schedule share aligned labels and values in the card's top row. */
@Composable
private fun FlightRecordingIdentity(flight: FlightDetail, loadCustomerLogo: (String) -> Flow<ByteArray?>,
    progressStatus: FlightProgressStatus, onTitlePositioned: (Float, Int) -> Unit, details: @Composable () -> Unit) {
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Surface(Modifier.fillMaxWidth(), color = Color.White, shape = RoundedCornerShape(28.dp)) {
            Column(Modifier.padding(16.dp)) {
                BoxWithConstraints(Modifier.fillMaxWidth()) {
                    val density = LocalDensity.current
                    val labelStyle = MaterialTheme.typography.labelSmall
                    val valueStyle = MaterialTheme.typography.titleLarge.copy(fontSize = 18.sp, lineHeight = 22.sp,
                        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
                    var scheduleHeight by remember { mutableIntStateOf(0) }
                    val logoSize = with(density) { if (scheduleHeight > 0) scheduleHeight.toDp() else 60.dp }
                    val stackSchedule = maxWidth < 76.dp + 210.dp * density.fontScale
                    val logo: @Composable () -> Unit = {
                        CustomerLogo(flight.customerId, loadCustomerLogo,
                            Modifier.size(logoSize).border(1.dp, Color(0xFFD1D1D6), RoundedCornerShape(10.dp)).padding(5.dp))
                    }
                    val number: @Composable (Modifier) -> Unit = { modifier ->
                        // The aircraft caption shares the date's third line and the logo's bottom edge.
                        Column(modifier.height(logoSize), verticalArrangement = Arrangement.SpaceBetween) {
                            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                Text(stringResource(R.string.flight_form_flt), style = labelStyle,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                Text(flight.flightNumber, Modifier.onGloballyPositioned {
                                    onTitlePositioned(it.positionInWindow().y, it.size.height)
                                }.semantics { heading() }, style = valueStyle,
                                    maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                            }
                            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp),
                                verticalAlignment = Alignment.CenterVertically) {
                                Icon(Icons.Outlined.Flight, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                                Text(flight.aircraftTypeModel?.takeIf { it.isNotBlank() }
                                    ?: stringResource(R.string.flights_card_aircraft_unavailable), Modifier.weight(1f),
                                    style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.Medium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                            }
                        }
                    }
                    val schedule: @Composable () -> Unit = {
                        FlightScheduleTimeGroup(flight, labelStyle, valueStyle,
                            Modifier.onSizeChanged { scheduleHeight = it.height })
                    }
                    if (stackSchedule) Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp),
                            verticalAlignment = Alignment.Top) {
                            logo()
                            number(Modifier.weight(1f))
                        }
                        Box(Modifier.align(Alignment.End)) { schedule() }
                    } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp),
                        verticalAlignment = Alignment.Top) {
                        logo()
                        number(Modifier.weight(1f))
                        schedule()
                    }
                }
                HorizontalDivider(Modifier.padding(vertical = 10.dp), thickness = 0.5.dp,
                    color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.06f))
                FlightOperationStatusRow(flight.operationTypeName, progressStatus)
                details()
            }
        }
    }
}

@Composable
internal fun FlightFormDateTimeField(value: String, label: String, description: String, enabled: Boolean = true,
    errorMessage: String? = null, onClear: (() -> Unit)? = null, clearDescription: String? = null, onClick: () -> Unit) {
    val locale = LocalLocale.current.platformLocale
    val dateTime = value.takeIf { it.isNotBlank() }?.let(LocalDateTime::parse)
    val hasClear = onClear != null && dateTime != null
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    FlightFormValidation(errorMessage) {
        Surface(onClick = { focus.clearFocus(); keyboard?.hide(); onClick() }, enabled = enabled, color = MaterialTheme.colorScheme.surface,
            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).semantics { contentDescription = description }) {
            // A trailing clear target owns the end inset; avoid adding the field's 14dp inset
            // and an extra row gap between the entered value and its close icon.
            BoxWithConstraints(Modifier.fillMaxWidth().alpha(if (enabled) 1f else 0.45f)
                .padding(start = 14.dp, end = if (hasClear) 0.dp else 14.dp,
                    top = if (hasClear) 0.dp else 6.dp, bottom = if (hasClear) 0.dp else 6.dp),
                contentAlignment = Alignment.Center) {
                val labelMaxWidth = maxWidth * 0.45f
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text(label, Modifier.padding(end = 8.dp).widthIn(max = labelMaxWidth),
                        style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                    FlightFormValueText(dateTime?.format(DateTimeFormatter.ofPattern("MMM d, yyyy · HH:mm", locale)) ?: "—",
                        Modifier.weight(1f))
                    if (hasClear) IconButton(onClick = {
                        focus.clearFocus(); keyboard?.hide(); onClear?.invoke()
                    }, enabled = enabled, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.Outlined.Close, clearDescription ?: description, Modifier.size(18.dp),
                            tint = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
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
    leadingPadding: Dp = 14.dp, enabled: Boolean = true, errorMessage: String? = null, onClick: () -> Unit) {
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    FlightFormValidation(errorMessage) {
        Surface(onClick = { focus.clearFocus(); keyboard?.hide(); onClick() }, enabled = enabled, color = MaterialTheme.colorScheme.surface,
            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).semantics { contentDescription = description }) {
            BoxWithConstraints(Modifier.fillMaxWidth().alpha(if (enabled) 1f else 0.45f).padding(start = leadingPadding, end = 14.dp, top = 6.dp, bottom = 6.dp), contentAlignment = Alignment.Center) {
                val labelMaxWidth = maxWidth * 0.45f
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(label, Modifier.widthIn(max = labelMaxWidth), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                    FlightFormValueText(value, Modifier.weight(1f), isPlaceholder)
                    if (enabled) Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, Modifier.size(18.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
}

@Composable
internal fun FlightFormTextRow(label: String, value: String, onChange: (String) -> Unit, placeholder: String,
    keyboardOptions: KeyboardOptions = KeyboardOptions(imeAction = ImeAction.Next), enabled: Boolean = true, errorMessage: String? = null) {
    FlightFormValidation(errorMessage) {
        BoxWithConstraints(Modifier.fillMaxWidth().alpha(if (enabled) 1f else 0.45f).heightIn(min = 48.dp).padding(horizontal = 14.dp)) {
            val labelMaxWidth = maxWidth * 0.45f
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(label, Modifier.widthIn(max = labelMaxWidth), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                FlightFormTextInput(value, onChange, placeholder, label, Modifier.weight(1f), keyboardOptions = keyboardOptions, enabled = enabled)
            }
        }
    }
}

/** Keep each failure attached to its own row, including rows inside a shared group. */
@Composable
internal fun FlightFormValidation(message: String?, content: @Composable () -> Unit) {
    Column(Modifier.fillMaxWidth()) {
        val outline = if (message == null) Modifier else Modifier
            .border(0.75.dp, MaterialTheme.colorScheme.error.copy(alpha = 0.2f), RoundedCornerShape(10.dp))
            .semantics { error(message) }
        Box(Modifier.fillMaxWidth().then(outline)) { content() }
        if (message != null) Text(message, Modifier.padding(start = 14.dp, end = 14.dp, top = 2.dp, bottom = 6.dp),
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
    }
}

@Composable
internal fun FlightFormTextInput(value: String, onChange: (String) -> Unit, placeholder: String, label: String,
    modifier: Modifier = Modifier, multiline: Boolean = false, textAlign: TextAlign = TextAlign.Right,
    keyboardOptions: KeyboardOptions = KeyboardOptions(imeAction = ImeAction.Next), enabled: Boolean = true) {
    val contentAlignment = if (textAlign == TextAlign.Left) {
        if (multiline) AbsoluteAlignment.TopLeft else AbsoluteAlignment.CenterLeft
    } else {
        if (multiline) AbsoluteAlignment.TopRight else AbsoluteAlignment.CenterRight
    }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    BasicTextField(value, onChange, enabled = enabled, modifier = modifier.fillMaxWidth().heightIn(min = 48.dp).flightFormEditableFocus()
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
internal fun FlightFormRemarks(value: String, onValueChange: (String) -> Unit, description: String, enabled: Boolean = true) {
    FlightFormFieldGroup {
        Column(Modifier.alpha(if (enabled) 1f else 0.45f).padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(stringResource(R.string.flight_form_remarks), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold)
            FlightFormTextInput(value, onValueChange, stringResource(R.string.flight_form_remarks_hint), description,
                multiline = true, textAlign = TextAlign.Left, keyboardOptions = KeyboardOptions(
                    capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done), enabled = enabled)
        }
    }
}

@Composable
internal fun FlightFormDateTimeSheet(value: LocalDateTime, referenceYear: Int, onValueChange: (LocalDateTime) -> Unit,
    onDismiss: () -> Unit, title: String = stringResource(R.string.flight_form_date_time), showTitle: Boolean = false) {
    FlightFormModalSheet(title, onDismiss) { close ->
        if (showTitle) Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 8.dp),
            verticalAlignment = Alignment.CenterVertically) {
            Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis)
            IconButton(onClick = close, modifier = Modifier.size(48.dp)) {
                Icon(Icons.Outlined.Close, stringResource(R.string.flight_form_close_picker), Modifier.size(20.dp))
            }
        }
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
