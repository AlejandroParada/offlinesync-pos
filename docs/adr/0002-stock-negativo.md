# La venta offline puede dejar el stock negativo

## Estado

Aceptada. 2026-11-10.

## Contexto

La venta ya ocurrió en el mostrador. Cuando la sucursal sincroniza, el stock central puede no alcanzar: se vendió de más, el catálogo estaba atrasado o dos sucursales descontaron el mismo saldo.

Hay que decidir qué hace la central con un hecho que ya pasó.

## Decisión

La venta se acepta igual. `StockPolicy` resta la cantidad y, si el saldo queda bajo cero, la venta sigue en estado `applied` y se inserta una fila en `stock_discrepancies` con el motivo `negative-stock` y el saldo resultante.

La política corre dentro de la transacción, sobre la fila recién leída. La columna de sistema `xmin` es el token de concurrencia: si otra venta modificó el mismo producto, el guardado falla y el cliente recibe `conflict` para reintentar. Así no se pierde un descuento ni se aplica dos veces.

## Alternativas descartadas

- Rechazar la sincronización. La venta ya se cobró. Rechazarla la esconde y el outbox reintenta para siempre.
- Recortar el saldo a cero. El faltante desaparece y el próximo ingreso de mercadería parte de un número falso.

## Consecuencias

- El stock central puede ser negativo hasta que entre mercadería. Eso es un dato, no un error de la API.
- La tabla de discrepancias es la auditoría: qué venta, qué producto, en cuánto quedó y por qué.
- Un producto que la central no conoce sí se rechaza (`rejected`). No se inventa stock para un id que no está en el catálogo, y el resto del lote no se frena.
