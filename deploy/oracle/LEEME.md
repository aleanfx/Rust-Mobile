# Servidor de Oxfall en Oracle Cloud (gratis)

Oracle Cloud "Always Free" da una máquina virtual gratis para siempre. Pide tarjeta solo para verificar; no cobra mientras uses lo marcado como *Always Free*.

## 1. Crear la cuenta
1. Entra a <https://www.oracle.com/cloud/free/> y crea la cuenta.
2. Elige como región de inicio una cercana (por ejemplo **Brazil East (Sao Paulo)** o **US East (Ashburn)**). La región no se puede cambiar después.

## 2. Crear la máquina
1. En el menú: **Compute → Instances → Create instance**.
2. **Image**: Ubuntu 24.04 (o 22.04).
3. **Shape**: *Ampere* `VM.Standard.A1.Flex` con 1 OCPU y 6 GB (Always Free). Si no hay capacidad, `VM.Standard.E2.1.Micro` (AMD, también gratis).
4. **SSH keys**: deja que genere el par de llaves y descarga la privada (o usa Cloud Shell, abajo).
5. **Create**. Anota la **Public IP address**.

## 3. Abrir los puertos 80 y 443
1. En la instancia: **Subnet → Security List (Default) → Add Ingress Rules**.
2. Source CIDR `0.0.0.0/0`, protocolo TCP, **Destination port** `80,443`. Guarda.

## 4. Instalar el servidor (un solo comando)
Conéctate por SSH (desde el PC, o desde el navegador con **Cloud Shell**, el icono `>_` arriba a la derecha):

```
ssh ubuntu@TU_IP
```

y pega:

```
curl -fsSL https://raw.githubusercontent.com/aleanfx/Rust-Mobile/claude/project-thread-v0it3s/deploy/oracle/setup.sh | sudo bash
```

Tarda unos minutos. Al final muestra la dirección del juego, por ejemplo `wss://129-151-1-2.sslip.io/ws`.

## 5. Probar
- En el navegador: `https://TU-IP-CON-GUIONES.sslip.io/` debe decir "Oxfall server · protocol …".
- La versión web: `https://aleanfx.github.io/Rust-Mobile/?server=wss://TU-IP-CON-GUIONES.sslip.io/ws`

Pásale la dirección `wss://…/ws` a Claude para dejarla fija en el juego (botón JUGAR EN LÍNEA).

## Mantenimiento
- Actualizar al último código: el mismo comando del paso 4.
- Ver qué pasa: `journalctl -u oxfall -f`
- La isla se reinicia sola cada jueves a las 19:00 UTC (wipe semanal).
