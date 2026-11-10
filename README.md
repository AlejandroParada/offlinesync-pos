# OfflineSync POS

Prototipo público, con datos de demostración, para explicar la sincronización offline de una sucursal de farmacia. Está en .NET 10 (LTS, soporte hasta noviembre de 2028). La sucursal vende sin red, reintenta cuando vuelve, y la central conserva una sola copia de la venta aunque el lote llegue otra vez. Si el saldo no alcanza, el stock queda negativo y la discrepancia queda registrada.

## Qué muestra

```text
Sucursal                         Central
SQLite outbox  -- POST /sync/sales -->  ASP.NET Core 10
reintento con la misma clave            caso de uso + regla de stock
                                        EF Core + PostgreSQL
```

En cada push y cada pull request, GitHub Actions compila, corre los tests y arma la imagen. En `main` la publica en GHCR.

## Línea para el CV

> OfflineSync POS (2026): API de sincronización offline-first en .NET 10 con endpoints idempotentes, EF Core/PostgreSQL, xUnit + Testcontainers, CI en GitHub Actions e imagen Docker en GHCR.

En el CV conviene decir ".NET" y no quedarse solo en ".NET Framework 4.7.2", para que se vea que conocés los dos mundos.

## Decisiones

1. **Idempotencia por venta.** La sucursal genera el GUID antes del primer envío y lo reutiliza. La central tiene un índice único. Reenviar el lote no vuelve a descontar stock. Detalle en [docs/adr/0001-idempotent-sync.md](docs/adr/0001-idempotent-sync.md).
2. **La venta ya ocurrió.** Si el saldo no alcanza, la venta se acepta y el stock puede quedar negativo. Queda una fila de discrepancia con motivo `negative-stock`. Rechazarla perdería una venta cobrada; recortar a cero escondería el faltante. Detalle en [docs/adr/0002-stock-negativo.md](docs/adr/0002-stock-negativo.md).

## Estructura

```text
offlinesync-pos/
├─ src/
│  ├─ OfflineSync.Domain/             ventas y regla de stock, sin infraestructura
│  ├─ OfflineSync.Application/        lote, estados applied/duplicate/rejected/conflict
│  ├─ OfflineSync.Infrastructure/     EF Core, migración, PostgreSQL
│  ├─ OfflineSync.Api/                Minimal API, OpenAPI, salud, Serilog
│  └─ OfflineSync.BranchClient/       worker con outbox SQLite
├─ tests/
│  ├─ OfflineSync.Domain.Tests/
│  ├─ OfflineSync.Application.Tests/
│  └─ OfflineSync.Api.IntegrationTests/   Testcontainers, se omiten si no hay Docker
├─ docs/adr/
├─ .github/workflows/ci.yml
├─ Dockerfile
├─ Dockerfile.branch
└─ docker-compose.yml
```

## Cómo levantarlo

Hace falta el SDK de .NET 10 y Docker.

```bash
docker compose up --build
```

Quedan tres procesos: PostgreSQL, la API en el puerto 8080 y el cliente de la sucursal Pocitos. El cliente encola tres ventas de demostración y las reintenta hasta que la API responde. Una de esas ventas (alcohol en gel, 3 unidades contra un saldo de 2) queda aplicada con discrepancia.

```bash
curl.exe http://localhost:8080/health
curl.exe http://localhost:8080/openapi/v1.json
```

Para repetir la venta del alcohol a mano está `src/OfflineSync.Api/OfflineSync.Api.http`. La segunda llamada responde `duplicate` y el saldo no se mueve otra vez.

La contraseña de Postgres del compose (`offlinesync`) es solo para esta demo local.

Sin Docker, con Postgres ya corriendo:

```bash
dotnet run --project src/OfflineSync.Api
dotnet run --project src/OfflineSync.BranchClient
```

La API escucha en `http://localhost:5080`. La cadena de conexión está en `src/OfflineSync.Api/appsettings.json`.

## Contrato

`POST /sync/sales`

```json
{
  "sales": [
    {
      "idempotencyKey": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2",
      "branchId": "pocitos",
      "occurredAt": "2026-11-01T15:00:00Z",
      "lines": [
        { "productId": "33333333-3333-3333-3333-333333333333", "quantity": 3, "unitPrice": 190 }
      ]
    }
  ]
}
```

La respuesta es 200 si el lote se pudo procesar. Cada ítem trae `applied`, `duplicate`, `rejected` o `conflict`. Un lote vacío, o de más de 100 ventas, responde 400.

`GET /health` incluye el chequeo de PostgreSQL. El documento OpenAPI está en `/openapi/v1.json`. En producción los logs de Serilog salen en JSON compacto por consola.

## Catálogo de demostración

| Producto | Id | Saldo inicial |
| --- | --- | --- |
| Ibuprofeno 400 mg | `11111111-1111-1111-1111-111111111111` | 20 |
| Amoxicilina 500 mg | `22222222-2222-2222-2222-222222222222` | 8 |
| Alcohol en gel 250 ml | `33333333-3333-3333-3333-333333333333` | 2 |

El cliente de sucursal repite estos ids. Si se agregan productos, hay que tocar `DemoCatalog` y `DemoSales` juntos.

## Tests

```bash
dotnet test
```

Dominio y aplicación no necesitan Docker. Los de integración levantan PostgreSQL con Testcontainers y, si Docker no está, se omiten.

## CI

`.github/workflows/ci.yml` corre en cada push y cada pull request: restore, build con analizadores del SDK, tests y `docker build`. La imagen se publica en `ghcr.io/<owner>/<repo>` solo en un push a `main`. El paquete del repositorio en GitHub tiene que permitir el `GITHUB_TOKEN` de Actions (`packages: write`).
