package com.nags.operations.data.network

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ApiLocalNetworkTest {
    @Test
    fun emulator_and_documented_local_address_ranges_require_permission() {
        for (host in listOf(
            "10.0.2.2", "10.0.3.2", "172.16.0.1", "172.31.255.254", "192.168.1.20",
            "169.254.1.1", "100.64.0.1", "100.127.255.254", "224.0.0.1", "255.255.255.255",
            "[fe80::1]", "[fc00::1]", "[fd00::1]", "[ff02::1]", "[::ffff:192.168.1.20]",
            "operations.local", "OPERATIONS.LOCAL.",
        )) {
            assertTrue(host, apiUsesLocalNetwork("http://$host:5213/"))
        }
    }

    @Test
    fun public_production_and_same_profile_loopback_endpoints_do_not_request_local_access() {
        for (host in listOf(
            "operation.nags-ksa.com", "80.209.234.61", "100.63.255.254", "100.128.0.1",
            "172.15.255.254", "172.32.0.1", "192.169.1.1", "[2001:4860:4860::8888]",
            "127.0.0.1", "localhost", "[::1]",
        )) {
            assertFalse(host, apiUsesLocalNetwork("https://$host/"))
        }
    }

    @Test
    fun malformed_or_absent_hosts_do_not_trigger_a_permission_prompt() {
        for (url in listOf("", "not a URL", "http://", "http://999.999.999.999/")) {
            assertFalse(url, apiUsesLocalNetwork(url))
        }
    }
}
