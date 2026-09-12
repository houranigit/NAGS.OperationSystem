package com.nags.operations.ui.account

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.ui.theme.BrandRed

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountScreen(viewModel: AccountViewModel, onBack: () -> Unit, onLogout: () -> Unit) {
    val state by viewModel.state.collectAsStateWithLifecycle()

    LifecycleResumeEffect(viewModel) {
        viewModel.refresh()
        onPauseOrDispose { }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.account_title), fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.account_back))
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = BrandRed,
                    titleContentColor = Color.White,
                    navigationIconContentColor = Color.White,
                ),
            )
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp),
        ) {
            state.profile?.let { profile ->
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(profile.displayName, style = MaterialTheme.typography.headlineSmall)
                    Text(profile.email, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    val enabled = state.profile?.receiveWorkOrderSubmissionEmails ?: false
                    val preferenceModifier = if (state.profile != null) {
                        Modifier.toggleable(
                            value = enabled,
                            enabled = state.canEditPreference,
                            role = Role.Switch,
                            onValueChange = viewModel::setReceiveWorkOrderSubmissionEmails,
                        )
                    } else {
                        Modifier
                    }
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .then(preferenceModifier),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Text(
                            stringResource(R.string.account_email_work_orders),
                            modifier = Modifier.weight(1f),
                            style = MaterialTheme.typography.titleMedium,
                        )
                        if (state.profile != null) {
                            Switch(checked = enabled, onCheckedChange = null, enabled = state.canEditPreference)
                        }
                    }
                    Text(
                        stringResource(R.string.account_email_work_orders_description),
                        style = MaterialTheme.typography.bodyMedium,
                    )
                    when {
                        state.isSaving || state.isLoading -> Row(
                            horizontalArrangement = Arrangement.spacedBy(12.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
                            Text(stringResource(if (state.isSaving) R.string.account_saving else R.string.account_loading))
                        }
                        state.error != null -> {
                            Text(
                                stringResource(
                                    if (state.error == AccountError.SaveUnconfirmed) R.string.account_save_unconfirmed
                                    else R.string.account_load_failed,
                                ),
                                color = MaterialTheme.colorScheme.error,
                            )
                            TextButton(onClick = viewModel::refresh, enabled = state.isOnline) {
                                Text(stringResource(R.string.account_refresh))
                            }
                        }
                        state.saved -> Text(stringResource(R.string.account_saved))
                    }
                    if (!state.isOnline) {
                        Text(
                            stringResource(R.string.account_offline),
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
            OutlinedButton(onClick = onLogout, enabled = !state.isSaving) {
                Icon(Icons.AutoMirrored.Filled.Logout, contentDescription = null)
                Text(stringResource(R.string.account_sign_out), modifier = Modifier.padding(start = 8.dp))
            }
        }
    }
}
