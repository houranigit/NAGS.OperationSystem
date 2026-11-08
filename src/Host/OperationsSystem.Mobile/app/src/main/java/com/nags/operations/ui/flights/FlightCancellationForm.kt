package com.nags.operations.ui.flights

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import java.time.LocalDate
import java.time.LocalDateTime
import kotlinx.coroutines.flow.Flow

/** Temporary cancellation entry only; saving and employee attribution remain unimplemented. */
@Composable
internal fun FlightCancellationForm(
    flight: FlightDetail,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onClose: () -> Unit,
) {
    val initialCanceledAt = rememberSaveable {
        LocalDateTime.now().withSecond(0).withNano(0).toString()
    }
    val referenceYear = rememberSaveable { LocalDate.now().year }
    var canceledAt by rememberSaveable { mutableStateOf(initialCanceledAt) }
    var remarks by rememberSaveable { mutableStateOf("") }
    var pickerOpen by rememberSaveable { mutableStateOf(false) }
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    val hasChanges = canceledAt != initialCanceledAt || remarks.isNotEmpty()
    val requestClose = {
        when {
            pickerOpen -> pickerOpen = false
            hasChanges -> confirmDiscard = true
            else -> onClose()
        }
    }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current

    FlightRecordingFormScaffold(
        flight = flight,
        title = stringResource(R.string.cancellation_title),
        saveDescription = stringResource(R.string.cancellation_save),
        loadCustomerLogo = loadCustomerLogo,
        onRequestClose = requestClose,
    ) {
        FlightFormFieldGroup {
            FlightFormDateTimeField(
                value = canceledAt,
                label = stringResource(R.string.cancellation_time),
                description = stringResource(R.string.cancellation_choose_time),
                onClick = {
                    focus.clearFocus()
                    keyboard?.hide()
                    pickerOpen = true
                },
            )
        }
        FlightFormTimeZoneHint()
        FlightFormRemarks(
            value = remarks,
            onValueChange = { remarks = it },
            description = stringResource(R.string.cancellation_remarks),
        )
    }

    if (pickerOpen) {
        FlightFormDateTimeSheet(
            value = LocalDateTime.parse(canceledAt),
            referenceYear = referenceYear,
            onValueChange = { canceledAt = it.toString() },
            onDismiss = { pickerOpen = false },
        )
    }
    if (confirmDiscard) {
        FlightFormDiscardDialog(
            title = stringResource(R.string.cancellation_discard_title),
            onDismiss = { confirmDiscard = false },
            onDiscard = onClose,
        )
    }
}
