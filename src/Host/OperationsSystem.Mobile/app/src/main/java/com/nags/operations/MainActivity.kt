package com.nags.operations

import android.Manifest
import android.annotation.SuppressLint
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.content.Intent
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.core.content.ContextCompat
import androidx.core.content.edit
import com.nags.operations.data.network.apiUsesLocalNetwork
import com.nags.operations.ui.OperationsApp
import com.nags.operations.ui.theme.OperationsTheme
import com.nags.operations.notifications.SystemNotificationManager

class MainActivity : ComponentActivity() {
    // The detector checks transitive Fragment versions; this activity never extends FragmentActivity.
    @SuppressLint("InvalidFragmentVersionForActivityResult")
    private val localNetworkPermissionRequest = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) AppGraph.get(applicationContext).onForeground()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        super.onCreate(savedInstanceState)
        requestLocalNetworkAccessIfNeeded()
        AppGraph.get(applicationContext).notificationNavigation.acceptIntent(
            intent,
            restoringActivity = savedInstanceState != null,
        )
        SystemNotificationManager(applicationContext).ensureChannel()

        // Transparent scrim + light status icons so clocks/notifications read on red headers.
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.light(
                Color.Transparent.toArgb(),
                Color.Transparent.toArgb(),
            ),
        )

        setContent {
            OperationsTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background,
                ) {
                    OperationsApp()
                }
            }
        }
    }

    private fun requestLocalNetworkAccessIfNeeded() {
        if (Build.VERSION.SDK_INT < 37 || !apiUsesLocalNetwork(BuildConfig.API_BASE_URL)) return
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_LOCAL_NETWORK) ==
            PackageManager.PERMISSION_GRANTED) return
        val preferences = getSharedPreferences("local_network_permission", MODE_PRIVATE)
        if (preferences.getBoolean("asked", false)) return
        preferences.edit { putBoolean("asked", true) }
        // A denial leaves the app available for cached reads. A later grant retries normal refreshes.
        localNetworkPermissionRequest.launch(Manifest.permission.ACCESS_LOCAL_NETWORK)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        AppGraph.get(applicationContext).notificationNavigation.acceptIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        AppGraph.get(applicationContext).onForeground()
    }
}
