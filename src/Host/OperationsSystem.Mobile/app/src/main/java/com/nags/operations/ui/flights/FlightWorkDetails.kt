package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import java.time.ZoneId
import com.nags.operations.R
import com.nags.operations.data.ResourceCatalogItem
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.data.taskCalculationType

/** Nested work choices keep each employee unique within the parent entry. */
@Composable
internal fun FlightWorkEmployeeList(
    items: List<FlightEmployeeDraft>,
    options: List<StaffCatalogItem>,
    showRemarks: Boolean,
    referenceYear: Int,
    zone: ZoneId,
    onChange: (List<FlightEmployeeDraft>) -> Unit,
    enabled: Boolean = true,
    errorMessage: String? = null,
    fieldErrors: Map<String, Int> = emptyMap(),
    timeBounds: FlightWorkTimeBounds? = null,
    defaultPeriod: FlightWorkPeriod? = null,
) {
    var pickerOpen by rememberSaveable { mutableStateOf(false) }
    var editingId by rememberSaveable { mutableStateOf<String?>(null) }
    var selectionHandled by rememberSaveable { mutableStateOf(false) }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val choices = remember(options) {
        options.sortedBy { it.fullName }.map {
            FlightWorkChoice(it.id, it.fullName, listOf(it.employeeId, it.manpowerTypeName).filter(String::isNotBlank).joinToString(" · "))
        }
    }
    fun openPicker(id: String?) {
        focus.clearFocus()
        keyboard?.hide()
        editingId = id
        selectionHandled = false
        pickerOpen = true
    }

    FlightFormValidation(errorMessage) {
        FlightWorkDetailsHeading(stringResource(R.string.work_employees, items.size))
    }
    Column(Modifier.fillMaxWidth().padding(start = 16.dp)) {
        items.forEach { item ->
            key(item.id) {
                FlightWorkNestedEntry {
                    FlightWorkRemovableEntry(item.id, stringResource(R.string.work_remove_item, item.employeeName),
                        onRemove = { onChange(items.filterNot { it.id == item.id }) }, compactDelete = true, enabled = enabled) {
                        FlightWorkChoiceRow("", item.employeeName.ifBlank { stringResource(R.string.flight_form_required_field) },
                            stringResource(R.string.work_select_employee), isPlaceholder = item.employeeId.isBlank(), leadingPadding = 0.dp, enabled = enabled,
                            errorMessage = fieldErrors["performers.${item.id}.staffMemberId"]?.let { stringResource(it) }, onClick = { openPicker(item.id) })
                    }
                    FlightFormDivider()
                    FlightWorkPeriodFields(item.period, referenceYear, zone,
                        onChange = { period -> onChange(items.map { if (it.id == item.id) it.copy(period = period) else it }) },
                        startEnabled = enabled, endEnabled = enabled,
                        startError = fieldErrors["performers.${item.id}.startUtc"]?.let { stringResource(it) },
                        endError = fieldErrors["performers.${item.id}.endUtc"]?.let { stringResource(it) }, timeBounds = timeBounds,
                        endRequired = timeBounds?.upperUtc != null)
                    if (showRemarks) FlightWorkNotes("", {}, item.remarks,
                        onRemarksChange = { remarks -> onChange(items.map { if (it.id == item.id) it.copy(remarks = remarks) else it }) },
                        description = stringResource(R.string.work_employee_remarks), showReference = false, enabled = enabled,
                        errorMessage = fieldErrors["performers.${item.id}.notes"]?.let { stringResource(it) })
                    FlightFormDivider()
                }
            }
        }
        FlightWorkAddRow(stringResource(R.string.work_add_employee, items.size + 1), enabled = enabled, onClick = { openPicker(null) })
    }
    if (pickerOpen && enabled) FlightWorkPicker(
        title = stringResource(R.string.work_select_employee),
        searchLabel = stringResource(R.string.work_search_employees),
        emptyLabel = stringResource(R.string.work_no_employees),
        options = choices.filter { choice -> items.none { it.employeeId == choice.id && it.id != editingId } },
        selectedId = items.firstOrNull { it.id == editingId }?.employeeId,
        onSelect = { id ->
            if (!selectionHandled) options.firstOrNull { it.id == id }?.let { selected ->
                selectionHandled = true
                onChange(if (editingId == null) items + FlightEmployeeDraft(employeeId = selected.id,
                    employeeName = selected.fullName, employeeCode = selected.employeeId,
                    period = defaultPeriod?.takeIf { it.start.isNotBlank() } ?: if (timeBounds != null)
                        seedBlankWorkStart(FlightWorkPeriod(), zone, timeBounds) else FlightWorkPeriod.now(zone))
                else items.map { if (it.id == editingId) it.copy(employeeId = selected.id,
                    employeeName = selected.fullName, employeeCode = selected.employeeId) else it })
            }
        },
        onDismiss = { pickerOpen = false; editingId = null },
    )
}

internal enum class FlightWorkResourceKind(
    val sectionLabel: Int,
    val itemLabel: Int,
    val addLabel: Int,
    val selectLabel: Int,
    val searchLabel: Int,
    val emptyLabel: Int,
) {
    Tool(R.string.work_tools, R.string.work_tool, R.string.work_add_tool, R.string.work_select_tool,
        R.string.work_search_tools, R.string.work_no_tools),
    Material(R.string.work_materials, R.string.work_material, R.string.work_add_material, R.string.work_select_material,
        R.string.work_search_materials, R.string.work_no_materials),
    GeneralSupport(R.string.work_general_support, R.string.work_support, R.string.work_add_support, R.string.work_select_support,
        R.string.work_search_supports, R.string.work_no_supports),
    ;

    val fieldKey: String get() = when (this) {
        Tool -> "tools"
        Material -> "materials"
        GeneralSupport -> "generalSupports"
    }

    val unknownResourceId: String get() = when (this) {
        Tool -> "60000000-0000-0000-0000-000000000001"
        Material -> "70000000-0000-0000-0000-000000000001"
        GeneralSupport -> "80000000-0000-0000-0000-000000000001"
    }
}

/** The selected catalog item determines whether its usage is quantity or duration. */
@Composable
internal fun FlightWorkResourceList(
    kind: FlightWorkResourceKind,
    items: List<FlightResourceDraft>,
    options: List<ResourceCatalogItem>,
    referenceYear: Int,
    zone: ZoneId,
    onChange: (List<FlightResourceDraft>) -> Unit,
    enabled: Boolean = true,
    fieldErrors: Map<String, Int> = emptyMap(),
    parentPeriod: FlightWorkPeriod = FlightWorkPeriod(),
    timeBounds: FlightWorkTimeBounds = FlightWorkTimeBounds(),
) {
    var pickerOpen by rememberSaveable(kind) { mutableStateOf(false) }
    var editingId by rememberSaveable(kind) { mutableStateOf<String?>(null) }
    var selectionHandled by rememberSaveable(kind) { mutableStateOf(false) }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val choices = remember(options) { options.sortedBy { it.name }.map { FlightWorkChoice(it.id, it.name) } }
    fun openPicker(id: String?) {
        focus.clearFocus()
        keyboard?.hide()
        editingId = id
        selectionHandled = false
        pickerOpen = true
    }

    FlightFormValidation(fieldErrors[kind.fieldKey]?.let { stringResource(it) }) {
        FlightWorkDetailsHeading(stringResource(kind.sectionLabel, items.size))
    }
    Column(Modifier.fillMaxWidth().padding(start = 16.dp)) {
        items.forEach { item ->
            key(item.id) {
                val prefix = "${kind.fieldKey}.${item.id}."
                val catalog = options.firstOrNull { it.id.equals(item.resourceId, true) }
                val calculationType = item.calculationType ?: catalog?.taskCalculationType()
                val reviewQuantity = taskResourceQuantityReviewRequired(item, calculationType)
                FlightWorkNestedEntry {
                    FlightWorkRemovableEntry(item.id, stringResource(R.string.work_remove_item, item.resourceName),
                        onRemove = { onChange(items.filterNot { it.id == item.id }) }, compactDelete = true, enabled = enabled) {
                        FlightWorkChoiceRow("", item.resourceName.ifBlank { stringResource(R.string.flight_form_required_field) },
                            stringResource(kind.selectLabel), isPlaceholder = item.resourceId.isBlank(), leadingPadding = 0.dp, enabled = enabled,
                            errorMessage = (fieldErrors[prefix + "resourceId"] ?: fieldErrors[prefix + "quantity"]?.takeIf { calculationType == 1 })
                                ?.let { stringResource(it) }, onClick = { openPicker(item.id) })
                    }
                    FlightFormDivider()
                    if (calculationType == 0) {
                        FlightFormTextRow(stringResource(R.string.work_quantity), item.quantity,
                            onChange = { quantity -> onChange(items.map { if (it.id == item.id) taskResourceQuantityEdited(it, quantity) else it }) },
                            placeholder = stringResource(R.string.flight_form_required_field), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                            enabled = enabled, errorMessage = if (reviewQuantity) stringResource(R.string.task_validation_quantity_review)
                                else fieldErrors[prefix + "quantity"]?.let { stringResource(it) })
                        if (reviewQuantity) FlightWorkPeriodFields(item.period, referenceYear, zone, onChange = {},
                            startEnabled = false, endEnabled = false, startRequired = false)
                    } else {
                        FlightWorkPeriodFields(item.period, referenceYear, zone,
                            onChange = { period -> onChange(items.map { if (it.id == item.id) it.copy(period = period) else it }) },
                            startEnabled = enabled, endEnabled = enabled,
                            startError = fieldErrors[prefix + "startUtc"]?.let { stringResource(it) },
                            endError = fieldErrors[prefix + "endUtc"]?.let { stringResource(it) }, timeBounds = timeBounds,
                            endRequired = timeBounds.upperUtc != null)
                    }
                    FlightWorkNotes("", {}, item.remarks,
                        onRemarksChange = { remarks -> onChange(items.map { if (it.id == item.id) it.copy(remarks = remarks) else it }) },
                        description = stringResource(R.string.work_resource_remarks), showReference = false, enabled = enabled,
                        errorMessage = fieldErrors[prefix + "notes"]?.let { stringResource(it) },
                        required = item.resourceId.equals(kind.unknownResourceId, ignoreCase = true))
                    FlightFormDivider()
                }
            }
        }
        FlightWorkAddRow(stringResource(kind.addLabel, items.size + 1), enabled = enabled, onClick = { openPicker(null) })
    }
    if (pickerOpen && enabled) FlightWorkPicker(
        title = stringResource(kind.selectLabel),
        searchLabel = stringResource(kind.searchLabel),
        emptyLabel = stringResource(kind.emptyLabel),
        options = choices.filter { choice -> items.none { it.resourceId.equals(choice.id, true) && it.id != editingId } },
        selectedId = items.firstOrNull { it.id == editingId }?.resourceId,
        onSelect = { id ->
            if (!selectionHandled) options.firstOrNull { it.id == id }?.let { selected ->
                selectionHandled = true
                onChange(if (editingId == null) items + taskResourceSelection(FlightResourceDraft(), selected, parentPeriod, zone, timeBounds)
                else items.map { if (it.id == editingId) taskResourceSelection(it, selected, parentPeriod, zone, timeBounds) else it })
            }
        },
        onDismiss = { pickerOpen = false; editingId = null },
    )
}

@Composable
private fun FlightWorkDetailsHeading(title: String) {
    Text(title, Modifier.fillMaxWidth().padding(start = 14.dp, top = 12.dp, end = 14.dp, bottom = 4.dp).semantics { heading() },
        style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurfaceVariant)
}
