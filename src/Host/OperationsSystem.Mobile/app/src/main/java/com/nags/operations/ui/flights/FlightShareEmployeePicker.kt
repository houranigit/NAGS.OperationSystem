package com.nags.operations.ui.flights

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.Checkbox
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.ui.components.InitialsAvatarStack
import java.text.Collator
import java.text.Normalizer
import java.util.Locale
import kotlinx.coroutines.launch

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

/** One scrolling surface keeps the flight context above a search field that pins while browsing employees. */
@OptIn(ExperimentalFoundationApi::class)
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
    headerContent: @Composable () -> Unit,
) {
    val ground = if (MaterialTheme.colorScheme.surface.luminance() > 0.5f) Color.White else MaterialTheme.colorScheme.surface
    val locale = LocalLocale.current.platformLocale
    val groups = remember(stationStaff, stationId, search, locale) {
        flightShareEmployeeGroups(stationStaff, stationId, search, locale)
    }
    val groupIndexes = remember(groups) {
        var next = 2 // Intro and pinned search.
        groups.associate { group -> group.letter to next.also { next += 1 + group.employees.size } }
    }
    val list = rememberLazyListState()
    val scope = rememberCoroutineScope()
    val keyboard = LocalSoftwareKeyboardController.current
    var searchFocused by remember { mutableStateOf(false) }
    var searchHeight by remember { mutableIntStateOf(0) }
    LaunchedEffect(searchFocused, search.isNotBlank()) {
        if (searchFocused || search.isNotBlank()) list.animateScrollToItem(1)
    }
    val visibleSearchBottom by remember(list) {
        derivedStateOf {
            list.layoutInfo.visibleItemsInfo.firstOrNull { it.key == "employee-search" }
                ?.let { (it.offset + it.size).coerceAtLeast(0) }
        }
    }
    val currentLetter by remember(list, groupIndexes) {
        derivedStateOf { groupIndexes.entries.lastOrNull { it.value <= list.firstVisibleItemIndex }?.key }
    }
    BoxWithConstraints(modifier.background(ground)) {
        LazyColumn(state = list, modifier = Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 16.dp)) {
            item(key = "share-intro", contentType = "intro") { headerContent() }
            stickyHeader(key = "employee-search", contentType = "search") {
                Box(Modifier.fillMaxWidth().background(ground)
                    .onSizeChanged { searchHeight = it.height }.padding(horizontal = 16.dp, vertical = 8.dp)) {
                    OutlinedTextField(value = search, onValueChange = onSearchChange, enabled = enabled,
                        modifier = Modifier.fillMaxWidth().onFocusChanged { searchFocused = it.isFocused },
                        singleLine = true, shape = RoundedCornerShape(28.dp),
                        placeholder = { Text(stringResource(R.string.flight_share_find_employee)) },
                        leadingIcon = { Icon(Icons.Outlined.Search, null, Modifier.size(22.dp)) },
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                        keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedContainerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
                            disabledContainerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
                            focusedBorderColor = MaterialTheme.colorScheme.primary,
                            unfocusedBorderColor = Color.Transparent, disabledBorderColor = Color.Transparent,
                        ))
                }
            }
            if (groups.isEmpty()) item(key = "no-employees", contentType = "empty") {
                Text(stringResource(R.string.flight_share_no_employees), Modifier.padding(horizontal = 16.dp, vertical = 20.dp),
                    style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            groups.forEach { group ->
                item(key = "letter:${group.letter}", contentType = "letter") {
                    Column(Modifier.fillMaxWidth().padding(start = 16.dp, end = 48.dp, top = 12.dp, bottom = 4.dp)) {
                        Text(group.letter, Modifier.padding(bottom = 6.dp).semantics { heading() },
                            style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                    }
                }
                items(group.employees, key = { "employee:${it.id}" }, contentType = { "employee" }) { employee ->
                    Column {
                        Row(Modifier.fillMaxWidth().heightIn(min = 64.dp)
                            .toggleable(value = employee.id in selected, enabled = enabled, role = Role.Checkbox,
                                onValueChange = { onToggle(employee.id) })
                            .padding(start = 16.dp, end = 44.dp, top = 8.dp, bottom = 8.dp),
                            horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                            Box(Modifier.clearAndSetSemantics { }) {
                                InitialsAvatarStack(listOf(employee.fullName), avatarSize = 40.dp, colorOffset = employee.id.hashCode())
                            }
                            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                                Text(employee.fullName, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium,
                                    maxLines = 2, overflow = TextOverflow.Ellipsis)
                                Text(employee.manpowerTypeName.ifBlank { employee.employeeId },
                                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    maxLines = 2, overflow = TextOverflow.Ellipsis)
                            }
                            Checkbox(checked = employee.id in selected, onCheckedChange = null, enabled = enabled)
                        }
                        HorizontalDivider(Modifier.padding(start = 68.dp, end = 48.dp),
                            color = MaterialTheme.colorScheme.outlineVariant)
                    }
                }
            }
        }
        val searchBottom = visibleSearchBottom
        if (searchBottom != null && groups.size > 1) {
            val top = with(LocalDensity.current) { searchBottom.toDp() }
            val available = (maxHeight - top).coerceAtLeast(0.dp)
            if (available >= 28.dp) Column(Modifier.align(Alignment.TopEnd).offset(y = top)
                .width(36.dp).heightIn(max = available).verticalScroll(rememberScrollState()).padding(vertical = 4.dp),
                horizontalAlignment = Alignment.CenterHorizontally) {
                groups.forEach { group ->
                    val label = stringResource(R.string.flight_share_jump_to_letter, group.letter)
                    Box(Modifier.width(36.dp).heightIn(min = 28.dp)
                        .clickable(enabled = enabled, role = Role.Button, onClickLabel = label) {
                            scope.launch { list.animateScrollToItem(groupIndexes.getValue(group.letter), -searchHeight) }
                        }.semantics { contentDescription = label }, contentAlignment = Alignment.Center) {
                        Text(group.letter, style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.primary,
                            fontWeight = if (currentLetter == group.letter) FontWeight.Bold else FontWeight.Medium)
                    }
                }
            }
        }
    }
}
