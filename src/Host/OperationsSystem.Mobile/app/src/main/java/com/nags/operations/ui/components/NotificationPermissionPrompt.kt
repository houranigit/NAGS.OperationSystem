package com.nags.operations.ui.components

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.core.content.ContextCompat
import com.nags.operations.R
import com.nags.operations.BuildConfig

@Composable
fun NotificationPermissionPrompt() {
    if (Build.VERSION.SDK_INT < 33 || !BuildConfig.FIREBASE_CONFIGURED) return
    val context = LocalContext.current
    val preferences = remember { context.getSharedPreferences("notification_permission", Context.MODE_PRIVATE) }
    val shouldPrompt = remember {
        mutableStateOf(
            !preferences.getBoolean("asked", false) &&
                ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED,
        )
    }
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        preferences.edit().putBoolean("asked", true).apply()
        shouldPrompt.value = false
    }
    if (!shouldPrompt.value) return

    AlertDialog(
        onDismissRequest = {
            preferences.edit().putBoolean("asked", true).apply()
            shouldPrompt.value = false
        },
        title = { Text(stringResource(R.string.notifications_permission_title)) },
        text = { Text(stringResource(R.string.notifications_permission_body)) },
        confirmButton = {
            TextButton(onClick = { launcher.launch(Manifest.permission.POST_NOTIFICATIONS) }) {
                Text(stringResource(R.string.notifications_permission_allow))
            }
        },
        dismissButton = {
            TextButton(onClick = {
                preferences.edit().putBoolean("asked", true).apply()
                shouldPrompt.value = false
            }) { Text(stringResource(R.string.notifications_permission_not_now)) }
        },
    )
}
