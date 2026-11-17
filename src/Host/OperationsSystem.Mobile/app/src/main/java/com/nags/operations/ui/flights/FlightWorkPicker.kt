package com.nags.operations.ui.flights

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.SheetValue
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.layout
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.Velocity
import com.nags.operations.R
import com.nags.operations.ui.components.InitialsAvatarStack
import kotlin.math.roundToInt
import kotlinx.coroutines.launch

/** Cached single choices; selecting a row changes its temporary form entry and closes this sheet. */
@Composable
@OptIn(ExperimentalMaterial3Api::class)
internal fun FlightWorkPicker(
    title: String,
    searchLabel: String,
    emptyLabel: String,
    options: List<FlightWorkChoice>,
    selectedId: String?,
    onSelect: (String) -> Unit,
    onDismiss: () -> Unit,
    searchable: Boolean = true,
) {
    var search by rememberSaveable(title) { mutableStateOf("") }
    var selecting by remember { mutableStateOf(false) }
    var closing by remember { mutableStateOf(false) }
    var dismissed by remember { mutableStateOf(false) }
    var searchFocused by remember { mutableStateOf(false) }
    var keyboardShown by remember { mutableStateOf(false) }
    var dismissSearchRequest by remember { mutableIntStateOf(0) }
    var searchBounds by remember { mutableStateOf<Rect?>(null) }
    var contentOrigin by remember { mutableStateOf(Offset.Zero) }
    val currentDismiss by rememberUpdatedState(onDismiss)
    val scope = rememberCoroutineScope()
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = !searchable, confirmValueChange = { value ->
        if (!closing && value != SheetValue.Expanded && (searchFocused || keyboardShown)) {
            dismissSearchRequest++
            false
        } else true
    })
    fun finishDismiss() {
        if (!dismissed) {
            dismissed = true
            currentDismiss()
        }
    }
    val locale = LocalLocale.current.platformLocale
    val groups = remember(options, search, searchable, locale) {
        if (searchable) flightWorkChoiceGroups(options, search, locale)
        else listOf(FlightWorkChoiceGroup("all", "", options)).filter { it.choices.isNotEmpty() }
    }
    val list = rememberLazyListState()
    val indexTargets = remember(groups, searchable, locale) {
        buildMap<String, Int> {
            if (searchable) {
                var itemIndex = 0
                groups.forEach { group ->
                    val letter = flightPickerIndexLetter(group.title, locale)
                    if (letter !in this) put(letter, itemIndex)
                    itemIndex += 1 + group.choices.size
                }
            }
        }
    }
    val currentLetter by remember(list, indexTargets) {
        derivedStateOf { indexTargets.entries.lastOrNull { it.value <= list.firstVisibleItemIndex }?.key }
    }
    LaunchedEffect(search) { list.scrollToItem(0) }
    val ground = flightPickerPageBackground()
    val flingBoundary = remember(sheet, list) {
        object : NestedScrollConnection {
            override suspend fun onPostFling(consumed: Velocity, available: Velocity): Velocity {
                val atExpanded = runCatching { sheet.requireOffset() <= 0.5f }.getOrDefault(false)
                return if (available.y > 0f && list.firstVisibleItemIndex == 0 && list.firstVisibleItemScrollOffset == 0 && atExpanded)
                    Velocity(0f, available.y) else Velocity.Zero
            }
        }
    }
    ModalBottomSheet(onDismissRequest = ::finishDismiss, sheetState = sheet,
        shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp), containerColor = ground,
        sheetGesturesEnabled = !searchFocused && !keyboardShown && !closing,
        dragHandle = null, tonalElevation = 0.dp, scrimColor = Color.Black.copy(alpha = 0.28f)) {
        val focus = LocalFocusManager.current
        val keyboard = LocalSoftwareKeyboardController.current
        val imeVisible = WindowInsets.ime.getBottom(LocalDensity.current) > 0
        fun hideSearchKeyboard() {
            focus.clearFocus()
            keyboard?.hide()
        }
        val close: () -> Unit = {
            if (!closing) {
                closing = true
                hideSearchKeyboard()
                scope.launch { sheet.hide(); finishDismiss() }
            }
        }
        LaunchedEffect(imeVisible) {
            val wasVisible = keyboardShown
            keyboardShown = imeVisible
            if (wasVisible && !imeVisible) focus.clearFocus()
            if (imeVisible && !closing && sheet.targetValue != SheetValue.Expanded)
                scope.launch { if (!closing) sheet.expand() }
        }
        LaunchedEffect(dismissSearchRequest) { if (dismissSearchRequest > 0) hideSearchKeyboard() }
        BackHandler(enabled = !closing && (searchFocused || imeVisible)) { hideSearchKeyboard() }
        val bodyModifier = if (searchable) Modifier.fillMaxWidth().fillMaxHeight()
            else Modifier.fillMaxWidth().heightIn(max = 600.dp)
        Column(bodyModifier.background(ground).semantics { paneTitle = title }
            .onGloballyPositioned { contentOrigin = it.positionInWindow() }
            .pointerInput(focus, keyboard) {
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
                    val bounds = searchBounds
                    if (searchFocused && isTap && bounds != null && !bounds.contains(release + contentOrigin))
                        hideSearchKeyboard()
                }
            }) {
            val expanded = sheet.targetValue == SheetValue.Expanded
            val handleLabel = stringResource(if (searchFocused || imeVisible) R.string.flight_picker_hide_keyboard
                else if (expanded) R.string.flight_picker_collapse else R.string.flight_picker_expand)
            Box(Modifier.fillMaxWidth().height(24.dp).clickable(enabled = !closing, role = Role.Button, onClickLabel = handleLabel) {
                if (searchFocused || imeVisible) hideSearchKeyboard()
                else scope.launch { if (expanded) sheet.partialExpand() else sheet.expand() }
            }.semantics { contentDescription = handleLabel }, contentAlignment = Alignment.Center) {
                Box(Modifier.size(width = 32.dp, height = 4.dp)
                    .background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.2f), CircleShape))
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.size(48.dp))
                Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold, textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
                IconButton(onClick = close, enabled = !selecting && !closing, modifier = Modifier.size(48.dp)) {
                    Box(Modifier.size(30.dp).background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.06f), CircleShape),
                        contentAlignment = Alignment.Center) {
                        Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel), Modifier.size(18.dp))
                    }
                }
            }
            if (searchable) FlightTabSearchHeader(isSearchOpen = true, query = search, onQueryChange = { search = it },
                onOpenSearch = {}, onCloseSearch = {}, searchDescription = searchLabel, placeholder = searchLabel,
                showCloseButton = false, autoFocusOnOpen = false, interactionEnabled = !selecting && !closing,
                onSearchFocusChanged = { focused ->
                    searchFocused = focused
                    if (focused && !closing) scope.launch { if (!closing) sheet.expand() }
                }, onSearchBoundsChanged = { searchBounds = it },
                showDividers = false, searchSurface = flightPickerCardBackground(), tabs = {})
            val viewportModifier = if (searchable) Modifier.fillMaxWidth().weight(1f).layout { measurable, constraints ->
                // Keep native anchors/header stable; only the list/index adapts to the visible viewport.
                val offset = runCatching { sheet.requireOffset() }.getOrDefault(0f).roundToInt().coerceAtLeast(0)
                val visibleHeight = (constraints.maxHeight - offset).coerceIn(0, constraints.maxHeight)
                val placeable = measurable.measure(constraints.copy(minHeight = 0, maxHeight = visibleHeight))
                layout(constraints.maxWidth, constraints.maxHeight) { placeable.placeRelative(0, 0) }
            } else Modifier.fillMaxWidth().heightIn(max = 440.dp)
            BoxWithConstraints(viewportModifier) {
                LazyColumn(state = list, modifier = (if (searchable) Modifier.fillMaxSize() else Modifier.fillMaxWidth()).nestedScroll(flingBoundary),
                    contentPadding = PaddingValues(bottom = 16.dp)) {
                    if (groups.isEmpty()) item(key = "empty") {
                        Text(if (options.isEmpty()) emptyLabel else stringResource(R.string.arrival_no_results),
                            Modifier.padding(horizontal = 20.dp, vertical = 20.dp), style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    groups.forEach { group ->
                        if (searchable) item(key = "group:${group.key}", contentType = "heading") {
                            Text(group.title, Modifier.fillMaxWidth().padding(start = 32.dp, end = 32.dp, top = 12.dp, bottom = 6.dp)
                                .semantics { heading() }, style = MaterialTheme.typography.labelLarge,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        itemsIndexed(group.choices, key = { _, choice -> "choice:${choice.id}" }, contentType = { _, _ -> "choice" }) { index, choice ->
                            val selected = choice.id == selectedId
                            val shape = RoundedCornerShape(topStart = if (index == 0) 20.dp else 0.dp,
                                topEnd = if (index == 0) 20.dp else 0.dp,
                                bottomStart = if (index == group.choices.lastIndex) 20.dp else 0.dp,
                                bottomEnd = if (index == group.choices.lastIndex) 20.dp else 0.dp)
                            Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp).clip(shape).background(flightPickerCardBackground())) {
                                Row(Modifier.fillMaxWidth().heightIn(min = 52.dp)
                                    .selectable(selected = selected, enabled = !selecting && !closing, role = Role.RadioButton, onClick = {
                                        if (!selecting && !closing) {
                                            selecting = true
                                            onSelect(choice.id)
                                            focus.clearFocus()
                                            keyboard?.hide()
                                            close()
                                        }
                                    }).padding(start = 16.dp, end = 12.dp, top = 6.dp, bottom = 6.dp),
                                    horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                                    choice.avatarName?.let { name ->
                                        InitialsAvatarStack(listOf(name), avatarSize = 32.dp, colorOffset = choice.id.hashCode(),
                                            modifier = Modifier.clearAndSetSemantics { })
                                    }
                                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                                        Text(choice.title, style = MaterialTheme.typography.bodyLarge,
                                            fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium,
                                            fontSize = 14.sp, lineHeight = 18.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                        if (choice.subtitle.isNotBlank()) Text(choice.subtitle, style = MaterialTheme.typography.bodySmall,
                                            fontSize = 11.sp, lineHeight = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant,
                                            maxLines = 2, overflow = TextOverflow.Ellipsis)
                                    }
                                    FlightPickerSelectionMark(selected, enabled = !selecting && !closing)
                                }
                                if (index < group.choices.lastIndex) HorizontalDivider(
                                    Modifier.padding(start = if (choice.avatarName != null) 60.dp else 16.dp, end = 12.dp), thickness = 0.5.dp,
                                    color = MaterialTheme.colorScheme.outlineVariant)
                            }
                        }
                    }
                }
                FlightListIndex(indexTargets.keys.toList(), currentLetter, enabled = !selecting && !closing) { letter ->
                    focus.clearFocus()
                    keyboard?.hide()
                    list.requestScrollToItem(indexTargets.getValue(letter))
                }
            }
        }
    }
}
