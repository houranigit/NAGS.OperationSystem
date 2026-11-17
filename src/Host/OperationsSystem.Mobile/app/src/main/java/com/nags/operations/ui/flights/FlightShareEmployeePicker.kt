package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInParent
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.ui.components.InitialsAvatarStack
import java.text.Collator
import java.text.Normalizer
import java.util.Locale
import kotlinx.coroutines.flow.first

internal data class FlightShareEmployeeGroup(val letter: String, val employees: List<StaffCatalogItem>)

/** Group the displayed full name, never an inferred surname; non-letter names belong to the final # section. */
internal fun flightShareEmployeeGroups(stationStaff: List<StaffCatalogItem>, stationId: String, search: String,
    locale: Locale = Locale.getDefault(),
): List<FlightShareEmployeeGroup> {
    if (stationId.isBlank()) return emptyList()
    val query = search.trim()
    val collator = Collator.getInstance(locale).apply { strength = Collator.PRIMARY }
    val comparator = Comparator<StaffCatalogItem> { first, second ->
        val name = collator.compare(first.fullName.trim(), second.fullName.trim())
        if (name != 0) name else {
            val employee = collator.compare(first.employeeId, second.employeeId)
            if (employee != 0) employee else first.id.compareTo(second.id)
        }
    }
    return stationStaff.filter { member ->
        member.stationId.equals(stationId, true) && (query.isEmpty() ||
            member.fullName.contains(query, true) || member.employeeId.contains(query, true) || member.manpowerTypeName.contains(query, true))
    }.distinctBy { it.id }.sortedWith(comparator).groupBy { member ->
        val name = Normalizer.normalize(member.fullName.trim(), Normalizer.Form.NFC)
        val first = name.takeIf { it.isNotEmpty() }?.codePointAt(0)
        if (first != null && Character.isLetter(first)) String(Character.toChars(first)).uppercase(locale) else "#"
    }.map { (letter, employees) -> FlightShareEmployeeGroup(letter, employees) }
        .sortedWith { first, second ->
            when {
                first.letter == second.letter -> 0
                first.letter == "#" -> 1
                second.letter == "#" -> -1
                else -> collator.compare(first.letter, second.letter).takeIf { it != 0 } ?: first.letter.compareTo(second.letter)
            }
        }
}

internal val FlightShareActionGreen = Color(0xFF167A32)

/** Page ground sampled from the reference, with the matching native dark surface. */
@Composable
internal fun flightSharePageBackground(): Color =
    if (MaterialTheme.colorScheme.surface.luminance() > 0.5f) Color(0xFFF1F1F1) else Color(0xFF1C1C1E)

/** Employee and selected-recipient card fill, distinct from the surrounding page. */
@Composable
internal fun flightShareListBackground(): Color =
    if (MaterialTheme.colorScheme.surface.luminance() > 0.5f) Color(0xFFFEFEFE) else Color(0xFF2C2C2E)

/** Search and selected recipients stay fixed above the independently scrolling contact list. */
@Composable
internal fun FlightShareEmployeePicker(
    stationStaff: List<StaffCatalogItem>,
    stationId: String,
    selected: Set<String>,
    search: String,
    onSearchChange: (String) -> Unit,
    onToggle: (String) -> Unit,
    enabled: Boolean,
    modifier: Modifier = Modifier,
) {
    val ground = flightSharePageBackground()
    val indexGreen = if (MaterialTheme.colorScheme.surface.luminance() > 0.5f) Color(0xFF167A32) else FlightFormGreen
    val locale = LocalLocale.current.platformLocale
    val groups = remember(stationStaff, stationId, search, locale) {
        flightShareEmployeeGroups(stationStaff, stationId, search, locale)
    }
    val groupIndexes = remember(groups) {
        var next = 0
        groups.associate { group -> group.letter to next.also { next += 1 + group.employees.size } }
    }
    val list = rememberLazyListState()
    val keyboard = LocalSoftwareKeyboardController.current
    val haptics = LocalHapticFeedback.current
    LaunchedEffect(search) { list.scrollToItem(0) }
    val currentLetter by remember(list, groupIndexes) {
        derivedStateOf { groupIndexes.entries.lastOrNull { it.value <= list.firstVisibleItemIndex }?.key }
    }
    Column(modifier.background(ground)) {
        val findEmployees = stringResource(R.string.flight_share_find_employee)
        FlightTabSearchHeader(isSearchOpen = true, query = search, onQueryChange = onSearchChange,
            onOpenSearch = {}, onCloseSearch = {}, modifier = Modifier.fillMaxWidth(),
            searchDescription = findEmployees, placeholder = findEmployees,
            showCloseButton = false, autoFocusOnOpen = false, interactionEnabled = enabled, showDividers = false, searchSurface = flightShareListBackground(), tabs = {})
        if (selected.isNotEmpty()) FlightShareSelectedEmployees(
            stationStaff = stationStaff.filter { it.stationId.equals(stationId, true) }, selected = selected,
            onRemove = onToggle, enabled = enabled,
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp).padding(bottom = 8.dp)
                .background(flightShareListBackground(), RoundedCornerShape(20.dp)))
        BoxWithConstraints(Modifier.fillMaxWidth().weight(1f)) {
            LazyColumn(state = list, modifier = Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 16.dp)) {
                if (groups.isEmpty()) item(key = "no-employees", contentType = "empty") {
                    Text(stringResource(R.string.flight_share_no_employees), Modifier.padding(horizontal = 16.dp, vertical = 20.dp),
                        style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                groups.forEach { group ->
                    item(key = "letter:${group.letter}", contentType = "letter") {
                        Text(group.letter, Modifier.fillMaxWidth().padding(start = 32.dp, end = 32.dp, top = 12.dp, bottom = 6.dp)
                            .semantics { heading() }, style = MaterialTheme.typography.labelLarge,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    itemsIndexed(group.employees, key = { _, employee -> "employee:${employee.id}" },
                        contentType = { _, _ -> "employee" }) { index, employee ->
                        val first = index == 0
                        val last = index == group.employees.lastIndex
                        val panelShape = RoundedCornerShape(
                            topStart = if (first) 20.dp else 0.dp, topEnd = if (first) 20.dp else 0.dp,
                            bottomStart = if (last) 20.dp else 0.dp, bottomEnd = if (last) 20.dp else 0.dp)
                        Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                            .clip(panelShape).background(flightShareListBackground())) {
                            Row(Modifier.fillMaxWidth().heightIn(min = 52.dp)
                                .toggleable(value = employee.id in selected, enabled = enabled, role = Role.Checkbox,
                                    onValueChange = { onToggle(employee.id) })
                                .padding(start = 16.dp, end = 12.dp, top = 6.dp, bottom = 6.dp),
                                horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                                Box(Modifier.clearAndSetSemantics { }) {
                                    InitialsAvatarStack(listOf(employee.fullName), avatarSize = 32.dp, colorOffset = employee.id.hashCode())
                                }
                                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                                    Text(employee.fullName, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium,
                                        fontSize = 14.sp, lineHeight = 18.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                    Text(employee.manpowerTypeName.ifBlank { employee.employeeId },
                                        style = MaterialTheme.typography.bodySmall, fontSize = 11.sp, lineHeight = 14.sp,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                }
                                FlightShareSelectionCircle(employee.id in selected, enabled)
                            }
                            if (!last) HorizontalDivider(Modifier.padding(start = 60.dp, end = 12.dp), thickness = 0.5.dp,
                                color = MaterialTheme.colorScheme.outlineVariant)
                        }
                    }
                }
            }
            if (groups.size > 1 && maxHeight >= 13.dp) {
                val letterBounds = remember(groups) { mutableMapOf<String, Rect>() }
                var touchedLetter by remember { mutableStateOf<String?>(null) }
                val naturalRowHeight = maxOf(13.dp, with(LocalDensity.current) { 11.sp.toDp() })
                val rowHeight = minOf(naturalRowHeight, maxHeight / groups.size)
                val textScale = rowHeight / naturalRowHeight
                fun jumpToLetter(letter: String) {
                    keyboard?.hide()
                    list.requestScrollToItem(groupIndexes.getValue(letter))
                }
                Column(Modifier.align(Alignment.CenterEnd).width(16.dp).height(rowHeight * groups.size)
                    .pointerInput(enabled, groups) {
                        if (!enabled) return@pointerInput
                        awaitEachGesture {
                            val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                            down.consume()
                            var lastLetter: String? = null
                            fun followFinger(y: Float) {
                                val letter = groups.minByOrNull { group ->
                                    letterBounds[group.letter]?.let { kotlin.math.abs(it.center.y - y) } ?: Float.POSITIVE_INFINITY
                                }?.letter ?: return
                                if (letter != lastLetter) {
                                    lastLetter = letter
                                    touchedLetter = letter
                                    jumpToLetter(letter)
                                    haptics.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
                                }
                            }
                            try {
                                followFinger(down.position.y)
                                do {
                                    val event = awaitPointerEvent(PointerEventPass.Initial)
                                    val change = event.changes.firstOrNull { it.id == down.id } ?: break
                                    change.consume()
                                    if (!change.pressed) break
                                    followFinger(change.position.y)
                                } while (true)
                            } finally {
                                touchedLetter = null
                            }
                        }
                    }, horizontalAlignment = Alignment.CenterHorizontally) {
                    groups.forEach { group ->
                        val label = stringResource(R.string.flight_share_jump_to_letter, group.letter)
                        Box(Modifier.width(16.dp).weight(1f)
                            .onGloballyPositioned { letterBounds[group.letter] = it.boundsInParent() }
                            .clickable(enabled = enabled, role = Role.Button, onClickLabel = label) {
                                jumpToLetter(group.letter)
                            }.semantics { contentDescription = label }, contentAlignment = Alignment.Center) {
                            Text(group.letter, style = MaterialTheme.typography.labelSmall, fontSize = 10.sp * textScale,
                                lineHeight = 11.sp * textScale, maxLines = 1, softWrap = false, color = indexGreen,
                                fontWeight = if ((touchedLetter ?: currentLetter) == group.letter) FontWeight.Bold else FontWeight.Medium)
                        }
                    }
                }
                touchedLetter?.let { letter ->
                    val bubbleSize = minOf(56.dp, maxHeight)
                    val letterIndex = groups.indexOfFirst { it.letter == letter }
                    val center = (maxHeight - rowHeight * groups.size) / 2 + rowHeight * (letterIndex + 0.5f)
                    val bubbleTop = (center - bubbleSize / 2).coerceIn(0.dp, maxOf(0.dp, maxHeight - bubbleSize))
                    Surface(Modifier.align(Alignment.TopEnd).offset(x = (-28).dp, y = bubbleTop)
                        .size(bubbleSize).clearAndSetSemantics { }, shape = CircleShape,
                        color = FlightShareActionGreen, contentColor = Color.White, shadowElevation = 4.dp) {
                        Box(contentAlignment = Alignment.Center) {
                            Text(letter, style = MaterialTheme.typography.headlineMedium, fontSize = 28.sp,
                                lineHeight = 32.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun FlightShareSelectionCircle(selected: Boolean, enabled: Boolean) {
    val selection = FlightShareActionGreen.copy(alpha = if (enabled) 1f else 0.38f)
    val border = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = if (enabled) 0.75f else 0.38f)
    Box(Modifier.size(20.dp)
        .background(if (selected) selection else Color.Transparent, CircleShape)
        .then(if (selected) Modifier else Modifier.border(1.25.dp, border, CircleShape))
        .clearAndSetSemantics { }, contentAlignment = Alignment.Center) {
        if (selected) Icon(Icons.Outlined.Check, null, Modifier.size(14.dp), tint = Color.White)
    }
}

/** The same selected-avatar tiles serve the picker strip and five-column message-page recipient group. */
@Composable
internal fun FlightShareSelectedEmployees(
    stationStaff: List<StaffCatalogItem>,
    selected: Set<String>,
    onRemove: (String) -> Unit,
    enabled: Boolean,
    modifier: Modifier = Modifier,
    onAdd: (() -> Unit)? = null,
    wrap: Boolean = false,
) {
    if (selected.isEmpty() && onAdd == null) return
    val byId = remember(stationStaff) { stationStaff.associateBy { it.id } }
    val fallback = stringResource(R.string.flight_share_employee)
    if (wrap) {
        val slots = buildList<String?> {
            if (onAdd != null) add(null)
            addAll(selected)
        }
        Column(modifier.padding(horizontal = 8.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            slots.chunked(5).forEach { row ->
                Row(Modifier.fillMaxWidth()) {
                    // Empty trailing slots retain the same column alignment on the final row.
                    repeat(5) { column ->
                        Box(Modifier.weight(1f), contentAlignment = Alignment.Center) {
                            if (column < row.size) {
                                val id = row[column]
                                if (id == null) onAdd?.let { FlightShareAddEmployeesTile(it, enabled) }
                                else FlightShareSelectedEmployeeTile(byId[id]?.fullName?.takeIf { it.isNotBlank() } ?: fallback,
                                    id, enabled, onRemove = { onRemove(id) })
                            }
                        }
                    }
                }
            }
        }
    } else {
        val orderedIds = selected.toList()
        val selectedList = rememberLazyListState()
        var previousIds by remember { mutableStateOf(orderedIds.toSet()) }
        LaunchedEffect(orderedIds) {
            val added = orderedIds.lastOrNull { it !in previousIds }
            previousIds = orderedIds.toSet()
            if (added != null) {
                // Wait for the new tile's layout; removals and unrelated recompositions never reposition the strip.
                val itemCount = orderedIds.size + if (onAdd != null) 1 else 0
                snapshotFlow { selectedList.layoutInfo.totalItemsCount }.first { it >= itemCount }
                selectedList.animateScrollToItem(orderedIds.indexOf(added))
            }
        }
        LazyRow(modifier, state = selectedList, contentPadding = PaddingValues(horizontal = 8.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            items(orderedIds, key = { it }) { id ->
                FlightShareSelectedEmployeeTile(byId[id]?.fullName?.takeIf { it.isNotBlank() } ?: fallback,
                    id, enabled, onRemove = { onRemove(id) })
            }
            if (onAdd != null) item(key = "add-employees") { FlightShareAddEmployeesTile(onAdd, enabled) }
        }
    }
}

@Composable
private fun FlightShareSelectedEmployeeTile(name: String, id: String, enabled: Boolean, onRemove: () -> Unit) {
    val remove = stringResource(R.string.flight_share_remove_employee, name)
    val firstName = remember(name) { name.trim().split(Regex("[\\p{Z}\\s]+"), limit = 2).firstOrNull().orEmpty() }
    Column(Modifier.width(60.dp).clickable(enabled = enabled, role = Role.Button, onClickLabel = remove, onClick = onRemove)
        .padding(vertical = 4.dp).semantics(mergeDescendants = true) { contentDescription = remove },
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Box(Modifier.size(44.dp).clearAndSetSemantics { }, contentAlignment = Alignment.Center) {
            InitialsAvatarStack(listOf(name), avatarSize = 40.dp, colorOffset = id.hashCode(), modifier = Modifier.clearAndSetSemantics { })
            Box(Modifier.align(Alignment.TopEnd).size(18.dp)
                .background(MaterialTheme.colorScheme.onSurfaceVariant, CircleShape)
                .border(2.dp, flightShareListBackground(), CircleShape), contentAlignment = Alignment.Center) {
                Icon(Icons.Outlined.Close, null, Modifier.size(12.dp), tint = MaterialTheme.colorScheme.surface)
            }
        }
        Text(firstName, modifier = Modifier.clearAndSetSemantics { }, maxLines = 1, overflow = TextOverflow.Ellipsis,
            style = MaterialTheme.typography.labelMedium, fontSize = 11.sp, lineHeight = 14.sp, color = MaterialTheme.colorScheme.onSurface)
    }
}

@Composable
private fun FlightShareAddEmployeesTile(onAdd: () -> Unit, enabled: Boolean) {
    val label = stringResource(R.string.flight_share_add_employees)
    Column(Modifier.width(60.dp).clickable(enabled = enabled, role = Role.Button, onClickLabel = label, onClick = onAdd)
        .padding(vertical = 4.dp).semantics(mergeDescendants = true) { contentDescription = label },
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Box(Modifier.size(44.dp), contentAlignment = Alignment.Center) {
            Box(Modifier.size(40.dp).background(FlightShareActionGreen.copy(alpha = if (enabled) 1f else 0.38f), CircleShape),
                contentAlignment = Alignment.Center) {
                Icon(Icons.Outlined.Add, null, Modifier.size(22.dp), tint = Color.White)
            }
        }
        Text(stringResource(R.string.flight_share_add_label), modifier = Modifier.clearAndSetSemantics { }, maxLines = 1, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.labelMedium, fontSize = 11.sp, lineHeight = 14.sp,
            color = MaterialTheme.colorScheme.onSurface)
    }
}
