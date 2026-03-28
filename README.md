# Rate Limiter

Challenge técnico para IOL (Invertir Online).

Implementé el capítulo 4 del libro "System Design Interview" de Alex Xu — 
un Rate Limiter usando el algoritmo Sliding Window Counter.

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Nada más — no necesita Redis ni Docker (se puede implementar a futuro)

## Cómo correrlo
```
git clone <url-del-repo>
cd RateLimiter
dotnet run --project RateLimiter
```

Se abre el browser en Swagger: `http://localhost:5083/swagger`

## Endpoints disponibles

| Endpoint | Límite | Descripción |
|---|---|---|
| `/Test` | 10 req/min | Endpoint general |
| `/Test/ordenes` | 3 req/min | Límite estricto — operación crítica |
| `/Test/cotizaciones` | 15 req/min | Límite permisivo — solo consulta |
| `/Test/metrics` | Sin límite | Métricas en tiempo real |

## Cómo probarlo

Abrí Swagger y ejecutá `/Test` más de 10 veces seguidas — 
a partir del request 11 vas a ver un 429.
Podés también probar los otros endpoints con sus límites respectivos. 
El endpoint de métricas muestra en tiempo real la cantidad de requests permitidos, bloqueados y el total.

O desde la terminal:
```
for i in {1..15}; do curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5083/Test; done
```

## Tests
```
dotnet test
```

## Decisiones de diseño

Ver [DESIGN.md](RateLimiter/DESIGN.md)