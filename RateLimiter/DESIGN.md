# Rate Limiter — Decisiones de Diseño

## El problema

Elegí el problema del rate limiting porque es un caso de uso real y crítico 
en empresas financieras como IOL. Un broker necesita proteger sus endpoints 
de abusos, scraping agresivo y picos de tráfico que podrían afectar la 
operatoria de otros clientes. Me pareció el problema más afín al negocio 
y el que mejor me permitía mostrar decisiones de diseño concretas y 
justificadas.

Implementar un "rate limiter" limita la cantidad de requests que un cliente
puede realizar a una API dentro de una ventana de tiempo configurable.

## Algoritmo elegido: Sliding Window Counter

Antes de escribir código evalué los algoritmos que habla en el libro "System Design Interview" capitulo 4:

**Token bucket** — tenés un balde con tokens. Cada request consume un token. Los tokens se recargan a una tasa fija. Permite bursts controlados.

**Leaking bucket** — similar al token bucket pero al revés. Los requests entran a una cola y salen a una tasa fija. Muy uniforme pero no tolera bursts.

**Fixed Window Counter** — el más simple. Divide el tiempo en ventanas fijas y
cuenta requests por ventana. Lo descarté porque tiene un problema conocido en los
bordes: un cliente puede hacer el doble de requests permitidos en un período corto
aprovechando el cambio de ventana. Por ejemplo, si el límite es 10 requests por
minuto, alguien puede hacer 10 requests a las 00:59 y otros 10 a las 01:01 —
20 requests en 2 segundos sin que nadie lo bloquee.

**Sliding Window Log** — el más preciso. Guarda el timestamp de cada request y
cuenta cuántos caen dentro del último período. Lo descarté porque el costo en
memoria crece con el volumen de requests, lo cual no escala bien.

**Sliding Window Counter** — el punto medio. Usa dos ventanas adyacentes y las
pondera según el tiempo transcurrido, no es que tras el periodo de tiempo se resetean las 10 request nuevamente, eso seria el Fixed Window, en este tiene un peso
y pasado el tiempo de ventana las request que se pueden hacer aumenta gradualmente hasta el maximo nuevamente:
```
estimado = requests_ventana_anterior * (tiempo_restante / tamaño_ventana) + requests_ventana_actual
```

## Por qué in-memory y no Redis

La primera versión que diseñé usaba Redis como backend distribuido. Lo descarté
por una razón concreta: cualquier persona que quiera revisar este código tendría
que instalar Redis o Docker para correrlo y no estava seguro si el que viera esto lo tendría. Por temas de tiempo decidí hacerlo in-memory.

Decidí usar un `ConcurrentDictionary` en memoria, y en esto me ayudó la IA. El prototipo corre con
`dotnet run` y nada más.

Lo que me interesa mostrar con esta decisión es que el diseño soporta el cambio
sin fricción. La interfaz `IRateLimiter` desacopla completamente el algoritmo
del mecanismo de almacenamiento:
```
IRateLimiter
SlidingWindowRL ← implementación actual (in-memory)
RedisRateLimiter ← implementación futura (distribuida)
```

Pasar a Redis en producción es un cambio de una línea en `Program.cs`:
```
// Hoy
builder.Services.AddSingleton<IRateLimiter, SlidingWindowRL>();

// En producción
builder.Services.AddSingleton<IRateLimiter, RedisRateLimiter>();
```

El middleware, los tests y la configuración no cambian. En cloud esto mapearía
a Azure Cache for Redis o AWS ElastiCache como estado compartido entre nodos.

## Otras decisiones que tomé

**Middleware sobre atributo de controller** — el rate limiter aplica a toda
la API desde un único punto de registro en `Program.cs`. Agregar un endpoint
nuevo no requiere recordar decorarlo.

**Fail open ante errores inesperados** — si el rate limiter falla por una
excepción no contemplada, el middleware deja pasar el request en lugar de
responder con un 500. Prefiero disponibilidad sobre control estricto en ese
caso. El error queda logueado.

**Limpieza periódica de memoria** — el `ConcurrentDictionary` tiene un `Timer`
que elimina ventanas expiradas cada `windowSize` segundos. Sin esto, la memoria
crecería indefinidamente con el tiempo.

**Singleton** — el rate limiter se registra como Singleton porque todo su estado
vive en el diccionario interno. No tiene sentido crear una instancia por request.

**Métricas** — agregué un endpoint `/Test/metrics` que expone en tiempo real
la cantidad de requests permitidos, bloqueados y el total. Los contadores usan
`Interlocked` para garantizar que el incremento sea atómico entre threads (Recomendacion de la IA)
concurrentes. El endpoint de métricas está excluido del rate limiting
deliberadamente


**Límites por endpoint** — cada endpoint puede tener su propio límite configurado
en `appsettings.json`. Por ejemplo, `/Test/ordenes` tiene un límite de 3 requests
por ventana porque es una operación crítica, mientras que `/Test/cotizaciones`
permite 15 porque es solo una consulta. Cualquier endpoint sin configuración
específica usa el límite global como fallback.

## Lo que dejé afuera y por qué

**Persistencia entre reinicios** — si la app se reinicia, los contadores se
pierden. En producción con Redis esto no pasaría. 

## Cómo usé la IA

Usé Claude como asistente, no como autor. Me ayudó en temas puntuales como:

- La sugerencia de usar `ConcurrentDictionary.AddOrUpdate` para garantizar
  que el incremento del contador sea atómico entre requests concurrentes
- La sugerencia del `Timer` para la limpieza periódica de ventanas viejas
- La sugerencia de usar `Interlocked` para los contadores de métricas
- La configuración de `WebApplicationFactory` para los tests de integración

También la utilicé para revisar bugs que encontré durante las pruebas manuales
y para definir la estructura inicial de los tests unitarios y de integración.
En ambos casos el código generado fue revisado, entendido y ajustado por mí.

## Bugs encontrados y corregidos

**Contador compartido entre endpoints** — durante las pruebas manuales noté que
al bloquear `/Test/ordenes` con 3 requests, `/Test` solo permitía 7 en lugar de 10.
El problema estaba en la clave del diccionario — no incluía el path del endpoint:
```
// Incorrecto — todos los endpoints compartían el mismo contador
var currentKey = $"{clientId}:{currentWindow}";

// Correcto — cada endpoint tiene su propio contador
var currentKey = $"{clientId}:{path}:{currentWindow}";
```

Lo detecté probando manualmente el comportamiento entre endpoints
