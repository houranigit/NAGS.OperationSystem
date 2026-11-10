package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
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
import androidx.compose.ui.unit.dp
import java.time.ZoneId
import com.nags.operations.R
import com.nags.operations.data.ResourceCatalogItem
import com.nags.operations.data.StaffCatalogItem

/** Nested form-only choices; repeated employee work periods remain separate entries. */
@Composable
internal fun FlightWorkEmployeeList(
    items: List<FlightEmployeeDraft>,
    options: List<StaffCatalogItem>,
    showRemarks: Boolean,
    referenceYear: Int,
    zone: ZoneId,
    onChange: (List<FlightEmployeeDraft>) -> Unit,
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

    FlightWorkDetailsHeading(stringResource(R.string.work_employees, items.size))
    items.forEachIndexed { index, item ->
        key(item.id) {
            FlightWorkRemovableEntry(item.id, stringResource(R.string.work_remove_item, item.employeeName),
                onRemove = { onChange(items.filterNot { it.id == item.id }) }, compactDelete = true) {
                FlightWorkChoiceRow(stringResource(R.string.work_staff_number, index + 1), item.employeeName,
                    stringResource(R.string.work_select_employee), leadingPadding = 0.dp, onClick = { openPicker(item.id) })
            }
            FlightFormDivider()
            FlightWorkPeriodFields(item.period, referenceYear, zone,
                onChange = { period -> onChange(items.map { if (it.id == item.id) it.copy(period = period) else it }) })
            if (showRemarks) FlightWorkNotes("", {}, item.remarks,
                onRemarksChange = { remarks -> onChange(items.map { if (it.id == item.id) it.copy(remarks = remarks) else it }) },
                description = stringResource(R.string.work_employee_remarks), showReference = false)
            FlightFormDivider()
        }
    }
    FlightWorkAddRow(stringResource(R.string.work_add_employee, items.size + 1), onClick = { openPicker(null) })
    if (pickerOpen) FlightWorkPicker(
        title = stringResource(R.string.work_select_employee),
        searchLabel = stringResource(R.string.work_search_employees),
        emptyLabel = stringResource(R.string.work_no_employees),
        options = choices,
        selectedId = items.firstOrNull { it.id == editingId }?.employeeId,
        onSelect = { id ->
            if (!selectionHandled) options.firstOrNull { it.id == id }?.let { selected ->
                selectionHandled = true
                onChange(if (editingId == null) items + FlightEmployeeDraft(employeeId = selected.id,
                    employeeName = selected.fullName, employeeCode = selected.employeeId, period = FlightWorkPeriod.now(zone))
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
}

/** Resources carry only their own work period and remarks; catalog calculation types are not inputs. */
@Composable
internal fun FlightWorkResourceList(
    kind: FlightWorkResourceKind,
    items: List<FlightResourceDraft>,
    options: List<ResourceCatalogItem>,
    referenceYear: Int,
    zone: ZoneId,
    onChange: (List<FlightResourceDraft>) -> Unit,
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

    FlightWorkDetailsHeading(stringResource(kind.sectionLabel, items.size))
    items.forEachIndexed { index, item ->
        key(item.id) {
            FlightWorkRemovableEntry(item.id, stringResource(R.string.work_remove_item, item.resourceName),
                onRemove = { onChange(items.filterNot { it.id == item.id }) }, compactDelete = true) {
                FlightWorkChoiceRow(stringResource(kind.itemLabel, index + 1), item.resourceName,
                    stringResource(kind.selectLabel), leadingPadding = 0.dp, onClick = { openPicker(item.id) })
            }
            FlightFormDivider()
            FlightWorkPeriodFields(item.period, referenceYear, zone,
                onChange = { period -> onChange(items.map { if (it.id == item.id) it.copy(period = period) else it }) })
            FlightWorkNotes("", {}, item.remarks,
                onRemarksChange = { remarks -> onChange(items.map { if (it.id == item.id) it.copy(remarks = remarks) else it }) },
                description = stringResource(R.string.work_resource_remarks), showReference = false)
            FlightFormDivider()
        }
    }
    FlightWorkAddRow(stringResource(kind.addLabel, items.size + 1), onClick = { openPicker(null) })
    if (pickerOpen) FlightWorkPicker(
        title = stringResource(kind.selectLabel),
        searchLabel = stringResource(kind.searchLabel),
        emptyLabel = stringResource(kind.emptyLabel),
        options = choices,
        selectedId = items.firstOrNull { it.id == editingId }?.resourceId,
        onSelect = { id ->
            if (!selectionHandled) options.firstOrNull { it.id == id }?.let { selected ->
                selectionHandled = true
                onChange(if (editingId == null) items + FlightResourceDraft(resourceId = selected.id, resourceName = selected.name, period = FlightWorkPeriod.now(zone))
                else items.map { if (it.id == editingId) it.copy(resourceId = selected.id, resourceName = selected.name) else it })
            }
        },
        onDismiss = { pickerOpen = false; editingId = null },
    )
}

@Composable
private fun FlightWorkDetailsHeading(title: String) {
    Text(title, Modifier.fillMaxWidth().padding(start = 14.dp, top = 12.dp, end = 14.dp, bottom = 4.dp).semantics { heading() },
        style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold)
}
