# Map source

`ne_110m_land.geojson` is the Natural Earth 1:110m land layer from the
[`nvkelso/natural-earth-vector`](https://github.com/nvkelso/natural-earth-vector)
repository. Natural Earth data is public domain.

It is a build input only. `make-world-map.js` projects it into the local SVG masks served by the
application; no map service or remote asset is contacted at runtime.
