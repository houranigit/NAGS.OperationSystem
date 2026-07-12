package com.nags.operations.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** Default rounded shape applied to every form field on the work-order screens (matches operations mobile). */
internal val FieldShape = RoundedCornerShape(14.dp)

/**
 * Inline searchable dropdown — the user types into the field itself and
 * matching options are shown directly underneath. Ported from
 * `OperationsApplication` [com.operations.mobile.ui.components.InlineSearchableDropdownField].
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun <T> InlineSearchableDropdownField(
    label: String,
    selectedText: String,
    placeholder: String,
    options: List<T>,
    renderOption: (T) -> String,
    onSelect: (T) -> Unit,
    modifier: Modifier = Modifier,
    readOnly: Boolean = false,
    isError: Boolean = false,
    supportingText: @Composable (() -> Unit)? = null,
    secondaryLine: ((T) -> String?)? = null,
    matches: ((T, String) -> Boolean)? = null,
) {
    var expanded by remember { mutableStateOf(false) }
    var query by remember(selectedText) { mutableStateOf(selectedText) }

    val filtered = remember(options, query) {
        if (query.isBlank()) options
        else options.filter { option ->
            val matcher = matches
            if (matcher != null) matcher(option, query)
            else renderOption(option).contains(query, ignoreCase = true) ||
                (secondaryLine?.invoke(option)?.contains(query, ignoreCase = true) == true)
        }
    }

    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { if (!readOnly) expanded = it },
        modifier = modifier.fillMaxWidth(),
    ) {
        OutlinedTextField(
            value = query,
            onValueChange = {
                query = it
                if (!readOnly) expanded = true
            },
            readOnly = readOnly,
            singleLine = true,
            shape = FieldShape,
            isError = isError,
            supportingText = supportingText,
            label = { Text(label) },
            placeholder = { Text(placeholder) },
            leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
            trailingIcon = {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    if (query.isNotEmpty() && !readOnly) {
                        IconButton(onClick = {
                            query = ""
                            expanded = true
                        }) {
                            Icon(Icons.Default.Close, contentDescription = "Clear")
                        }
                    }
                    ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded)
                }
            },
            modifier = Modifier
                .menuAnchor(MenuAnchorType.PrimaryEditable, enabled = !readOnly)
                .fillMaxWidth(),
        )
        if (filtered.isNotEmpty()) {
            ExposedDropdownMenu(
                expanded = expanded,
                onDismissRequest = { expanded = false },
            ) {
                filtered.forEach { option ->
                    DropdownMenuItem(
                        text = {
                            Column {
                                Text(
                                    renderOption(option),
                                    style = MaterialTheme.typography.bodyLarge,
                                    fontWeight = FontWeight.Medium,
                                )
                                val sub = secondaryLine?.invoke(option)
                                if (!sub.isNullOrBlank()) {
                                    Text(
                                        sub,
                                        style = MaterialTheme.typography.bodySmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    )
                                }
                            }
                        },
                        onClick = {
                            onSelect(option)
                            query = renderOption(option)
                            expanded = false
                        },
                    )
                }
            }
        }
    }
}

/**
 * Read-only field that opens a searchable bottom sheet where rows toggle with checkboxes.
 * Mirrors `OperationsApplication` task pickers (employees, tools, materials, general supports).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun <T> MultiSelectDropdownField(
    label: String,
    selectedSummary: String,
    placeholder: String,
    options: List<T>,
    selectedKeys: Set<String>,
    optionKey: (T) -> String,
    renderOption: (T) -> String,
    onSelectionChange: (Set<String>) -> Unit,
    modifier: Modifier = Modifier,
    readOnly: Boolean = false,
    isError: Boolean = false,
    supportingText: @Composable (() -> Unit)? = null,
    secondaryLine: ((T) -> String?)? = null,
    matches: ((T, String) -> Boolean)? = null,
) {
    var sheetOpen by remember { mutableStateOf(false) }

    Box(modifier = modifier.fillMaxWidth()) {
        OutlinedTextField(
            value = selectedSummary,
            onValueChange = {},
            readOnly = true,
            enabled = false,
            isError = isError,
            supportingText = supportingText,
            shape = FieldShape,
            modifier = Modifier
                .fillMaxWidth()
                .clickable(enabled = !readOnly) { sheetOpen = true },
            label = { Text(label) },
            placeholder = { Text(placeholder) },
            trailingIcon = {
                Icon(Icons.Default.ArrowDropDown, contentDescription = null)
            },
            colors = OutlinedTextFieldDefaults.colors(
                disabledTextColor = MaterialTheme.colorScheme.onSurface,
                disabledBorderColor = if (isError) {
                    MaterialTheme.colorScheme.error
                } else {
                    MaterialTheme.colorScheme.outline
                },
                disabledLabelColor = if (isError) {
                    MaterialTheme.colorScheme.error
                } else {
                    MaterialTheme.colorScheme.onSurfaceVariant
                },
                disabledTrailingIconColor = MaterialTheme.colorScheme.onSurfaceVariant,
                disabledPlaceholderColor = MaterialTheme.colorScheme.onSurfaceVariant,
            ),
        )
    }

    if (sheetOpen) {
        MultiSelectOptionSheet(
            title = label,
            options = options,
            initialSelectedKeys = selectedKeys,
            optionKey = optionKey,
            renderOption = renderOption,
            secondaryLine = secondaryLine,
            matches = matches,
            onDismiss = { sheetOpen = false },
            onApply = { keys ->
                onSelectionChange(keys)
                sheetOpen = false
            },
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun <T> MultiSelectOptionSheet(
    title: String,
    options: List<T>,
    initialSelectedKeys: Set<String>,
    optionKey: (T) -> String,
    renderOption: (T) -> String,
    secondaryLine: ((T) -> String?)?,
    matches: ((T, String) -> Boolean)?,
    onDismiss: () -> Unit,
    onApply: (Set<String>) -> Unit,
) {
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var query by remember { mutableStateOf("") }
    var localKeys by remember(initialSelectedKeys) {
        mutableStateOf(initialSelectedKeys.toMutableSet())
    }

    val filtered = remember(options, query) {
        if (query.isBlank()) options
        else options.filter { option ->
            val matcher = matches
            if (matcher != null) matcher(option, query)
            else renderOption(option).contains(query, ignoreCase = true) ||
                (secondaryLine?.invoke(option)?.contains(query, ignoreCase = true) == true)
        }
    }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = sheetState,
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .padding(horizontal = 20.dp, vertical = 4.dp),
        ) {
            Text(
                title,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.padding(bottom = 12.dp),
            )
            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                modifier = Modifier.fillMaxWidth(),
                placeholder = { Text("Search…") },
                leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                trailingIcon = {
                    if (query.isNotEmpty()) {
                        IconButton(onClick = { query = "" }) {
                            Icon(Icons.Default.Close, contentDescription = "Clear")
                        }
                    }
                },
                singleLine = true,
                shape = FieldShape,
            )
            Spacer(Modifier.size(12.dp))
            if (filtered.isEmpty()) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 24.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        if (query.isBlank()) "No options yet"
                        else "No results matching “$query”",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            } else {
                LazyColumn(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 440.dp),
                    verticalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    items(filtered, key = { optionKey(it) }) { option ->
                        val key = optionKey(option)
                        val checked = key in localKeys
                        Surface(
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(12.dp),
                            color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.35f),
                            border = BorderStroke(
                                1.dp,
                                MaterialTheme.colorScheme.outline.copy(alpha = 0.2f),
                            ),
                            onClick = {
                                localKeys = localKeys.toMutableSet().apply {
                                    if (checked) remove(key) else add(key)
                                }
                            },
                        ) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 8.dp, vertical = 6.dp),
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Checkbox(
                                    checked = checked,
                                    onCheckedChange = { now ->
                                        localKeys = localKeys.toMutableSet().apply {
                                            if (now) add(key) else remove(key)
                                        }
                                    },
                                )
                                Column(modifier = Modifier.weight(1f)) {
                                    Text(
                                        renderOption(option),
                                        style = MaterialTheme.typography.bodyLarge,
                                        fontWeight = FontWeight.Medium,
                                    )
                                    val sub = secondaryLine?.invoke(option)
                                    if (!sub.isNullOrBlank()) {
                                        Text(
                                            sub,
                                            style = MaterialTheme.typography.bodySmall,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Spacer(Modifier.size(12.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                OutlinedButton(
                    onClick = onDismiss,
                    modifier = Modifier.weight(1f),
                    shape = FieldShape,
                ) { Text("Cancel") }
                Button(
                    onClick = { onApply(localKeys.toSet()) },
                    modifier = Modifier.weight(1f),
                    shape = FieldShape,
                ) { Text("Done", fontWeight = FontWeight.SemiBold) }
            }
            Spacer(Modifier.size(8.dp))
        }
    }
}

fun formatMultiSelectSummary(labels: List<String>): String = when {
    labels.isEmpty() -> ""
    labels.size <= 2 -> labels.joinToString(", ")
    else -> "${labels.take(2).joinToString(", ")} +${labels.size - 2}"
}
