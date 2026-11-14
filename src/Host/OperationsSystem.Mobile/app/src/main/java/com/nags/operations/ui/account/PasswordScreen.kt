package com.nags.operations.ui.account

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind

@Composable
internal fun PasswordScreen(viewModel: PasswordViewModel, onBack: () -> Unit, onChanged: () -> Unit) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val currentFocus = remember { FocusRequester() }
    val replacementFocus = remember { FocusRequester() }
    val confirmationFocus = remember { FocusRequester() }
    var validationAttempt by remember { mutableIntStateOf(0) }
    val problems = if (state.attempted) passwordProblems(state) else emptySet()
    LaunchedEffect(state.changed) { if (state.changed) onChanged() }
    LaunchedEffect(validationAttempt) {
        if (validationAttempt > 0) when {
            PasswordProblem.CurrentRequired in problems -> currentFocus.requestFocus()
            PasswordProblem.Policy in problems -> replacementFocus.requestFocus()
            PasswordProblem.Confirmation in problems -> confirmationFocus.requestFocus()
        }
    }
    BackHandler(enabled = state.isSaving) { }
    Scaffold(topBar = { ProfilePageHeader(stringResource(R.string.profile_password), onBack = { if (!state.isSaving) onBack() }) }) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).imePadding().verticalScroll(rememberScrollState())
            .padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text(stringResource(R.string.password_change_notice), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (!state.isOnline) InlineNotice(stringResource(R.string.password_offline), kind = InlineNoticeKind.Info)
            state.error?.let { InlineNotice(it, kind = InlineNoticeKind.Error) }
            PasswordInput(stringResource(R.string.password_current), state.current, viewModel::current, currentFocus,
                enabled = !state.isSaving, error = if (PasswordProblem.CurrentRequired in problems) stringResource(R.string.password_current_required) else null)
            PasswordInput(stringResource(R.string.password_new), state.replacement, viewModel::replacement, replacementFocus,
                enabled = !state.isSaving, error = if (PasswordProblem.Policy in problems) stringResource(R.string.password_policy) else null,
                helper = stringResource(R.string.password_policy))
            PasswordInput(stringResource(R.string.password_confirm), state.confirmation, viewModel::confirmation, confirmationFocus,
                enabled = !state.isSaving, error = if (PasswordProblem.Confirmation in problems) stringResource(R.string.password_mismatch) else null)
            Button(onClick = { viewModel.save(); validationAttempt++ }, enabled = state.isOnline && !state.isSaving,
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp), shape = androidx.compose.foundation.shape.RoundedCornerShape(12.dp)) {
                if (state.isSaving) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onPrimary)
                Text(stringResource(R.string.password_update), Modifier.padding(horizontal = 8.dp), fontWeight = FontWeight.SemiBold)
            }
        }
    }
}

@Composable
private fun PasswordInput(label: String, value: String, onChange: (String) -> Unit, focus: FocusRequester,
    enabled: Boolean, error: String?, helper: String? = null) {
    var visible by remember { mutableStateOf(false) }
    OutlinedTextField(value, onChange, Modifier.fillMaxWidth().focusRequester(focus), enabled = enabled,
        label = { Text(label) }, singleLine = true, isError = error != null,
        visualTransformation = if (visible) VisualTransformation.None else PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
        trailingIcon = { IconButton(onClick = { visible = !visible }) {
            Icon(if (visible) Icons.Default.VisibilityOff else Icons.Default.Visibility,
                stringResource(if (visible) R.string.password_hide else R.string.password_show))
        } }, supportingText = (error ?: helper)?.let { message -> { Text(message) } })
}
