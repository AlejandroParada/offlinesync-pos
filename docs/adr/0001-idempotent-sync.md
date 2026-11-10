# Sincronización idempotente de ventas offline

## Estado

Aceptada. 2026-11-10.

## Contexto

Una sucursal vende aunque no tenga red. El cliente guarda la venta en un outbox SQLite y la reenvía cuando vuelve la conexión. El mismo lote puede llegar dos veces: por un timeout en el que la central sí grabó, por un reintento del proceso o por dos workers a la vez.

Una cola central (RabbitMQ, Service Bus) no resuelve el hueco. Si la sucursal está offline, el mensaje tiene que durar en la sucursal, no en un broker al que no llega.

## Decisión

Cada venta recibe un `Idempotency-Key` (GUID) en la sucursal **antes** del primer intento. Esa clave se guarda en el outbox y se reutiliza en todos los reintentos. No se genera una clave nueva por llamada HTTP.

`POST /sync/sales` recibe un lote. El resultado es por venta, no por request:

| Estado | Qué pasó |
| --- | --- |
| `applied` | La venta se grabó y el stock se movió. |
| `duplicate` | La clave ya existía. Se devuelve el resultado original y el stock no se toca. |
| `rejected` | Esa venta no se puede aplicar (producto desconocido, datos inválidos). El resto del lote sigue. |
| `conflict` | El stock cambió en paralelo. No se aplicó. El cliente reintenta. |

En PostgreSQL hay un índice único sobre `sales.idempotency_key`. El camino normal consulta la clave; si dos requests pasan la consulta a la vez, el índice único deja una sola fila y la otra se responde como `duplicate`.

## Consecuencias

- Reintentar es seguro. El cliente puede caerse a la mitad de un lote y volver a mandarlo.
- La clave tiene que nacer junto con la venta, no en el momento del POST. Si se regenera, la central cuenta dos ventas.
- El contrato HTTP es un lote, pero la idempotencia es por venta. Un 200 puede mezclar `applied` y `duplicate`.
- Hace falta otra decisión para el stock que queda negativo. Está en [0002](0002-stock-negativo.md).
