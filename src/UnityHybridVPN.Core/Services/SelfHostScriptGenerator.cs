namespace UnityHybridVPN.Core.Services;

/// <summary>
/// Generates a clean WireGuard setup script for user's own Ubuntu / Oracle Always Free VPS.
/// </summary>
public static class SelfHostScriptGenerator
{
    public static string GenerateUbuntuWireGuardScript(string serverPublicIp, int port = 51820)
    {
        string safeIp = string.IsNullOrWhiteSpace(serverPublicIp) ? "<YOUR_VPS_PUBLIC_IP>" : serverPublicIp.Trim();
        return $@"#!/usr/bin/env bash
# UnityHybridVPN — 1-Click WireGuard Self-Host Setup (Ubuntu / Oracle Always Free)
set -euo pipefail

sudo apt-get update && sudo apt-get install -y wireguard qrencode
umask 077
wg genkey | tee /etc/wireguard/server_private.key | wg pubkey > /etc/wireguard/server_public.key
wg genkey | tee /etc/wireguard/client_private.key | wg pubkey > /etc/wireguard/client_public.key

SERVER_PRIV=$(cat /etc/wireguard/server_private.key)
SERVER_PUB=$(cat /etc/wireguard/server_public.key)
CLIENT_PRIV=$(cat /etc/wireguard/client_private.key)
CLIENT_PUB=$(cat /etc/wireguard/client_public.key)
WAN_IF=$(ip route get 1.1.1.1 | awk '{{print $5; exit}}')

cat <<EOF | sudo tee /etc/wireguard/wg0.conf > /dev/null
[Interface]
Address = 10.66.0.1/24
ListenPort = {port}
PrivateKey = $SERVER_PRIV
PostUp = iptables -A FORWARD -i %i -j ACCEPT; iptables -t nat -A POSTROUTING -o $WAN_IF -j MASQUERADE
PostDown = iptables -D FORWARD -i %i -j ACCEPT; iptables -t nat -D POSTROUTING -o $WAN_IF -j MASQUERADE

[Peer]
PublicKey = $CLIENT_PUB
AllowedIPs = 10.66.0.2/32
EOF

echo 'net.ipv4.ip_forward=1' | sudo tee -a /etc/sysctl.conf > /dev/null
sudo sysctl -p
sudo systemctl enable wg-quick@wg0 && sudo systemctl restart wg-quick@wg0

echo ""=== COPY THIS CLIENT CONFIG INTO UnityHybridVPN ===""
cat <<EOF
[Interface]
PrivateKey = $CLIENT_PRIV
Address = 10.66.0.2/24
DNS = 1.1.1.1

[Peer]
PublicKey = $SERVER_PUB
Endpoint = {safeIp}:{port}
AllowedIPs = 0.0.0.0/0
PersistentKeepalive = 25
EOF
";
    }
}
