# وثيقة المعمارية البرمجية (`ARCHITECTURE.md`)
- **المشروع:** `PROJ-002-hybrid-vpn` (**UnityHybridVPN**) | **المعد:** `software-architect` | **اعتماد CTO:** `Approved (ثيم)`

## 1. النمط المعماري المختار
**Modular Hybrid VPN Client (`.NET 9` Core + `WPF Fluent UI`):**
- **`UnityHybridVPN.Core`:**
  - `ConfigSecurityValidator`: فحص ملفات `WireGuard (.conf)` و`OpenVPN (.ovpn)` لمنع حقن الأوامر (`--up`, `--route-up`, `PostUp` المشبوهة).
  - `VpnGateDirectoryService`: جلب وتحليل قائمة `VPNGate` (`CSV + Base64 .ovpn`) مع فاحص `TCP Ping` موازٍ.
  - `SelfHostScriptGenerator`: توليد سكربت `WireGuard` لخوادم `Ubuntu / Oracle Always Free`.
  - `TunnelController`: إدارة محركات `WireGuard` / `OpenVPN` المثبتة على النظام مع `Kill Switch` آمن عبر `Windows Firewall` واستعادة تلقائية.
- **`UnityHybridVPN.UI`:** واجهة `WPF Fluent Dark` ثنائية اللغة (`RTL/LTR`) مع `System Tray`.

## 2. بوابة التحقق (`GATE 02 — Architecture Validation`)
- **الحالة:** `PASS`
