# ConceptWave XML Script Lint

Bu örnek proje, ConceptWave XML içindeki `<Script>` bloklarını streaming olarak okuyup
JavaScript kurallarına göre uyarı üretir. XML büyük olduğunda `XmlReader` ile okuma
sayesinde bellek kullanımını düşük tutar.

## Kullanım

```
dotnet restore ConceptWaveLint/ConceptWaveLint.csproj
dotnet run --project ConceptWaveLint -- <input.xml> [lint-config.json]
```

Komut satırında XML yolu verilmezse uygulama sizden dosya yolunu ister.

Örnek:

```
dotnet restore ConceptWaveLint/ConceptWaveLint.csproj
dotnet run --project ConceptWaveLint -- metadata.xml lint-config.json
```

Restore işlemi `project.assets.json` dosyasını üretir. Build hatası alırsanız önce
`dotnet restore` çalıştırın veya Visual Studio'da **Restore NuGet Packages** kullanın.

## ESLint entegrasyonu

Varsayılan olarak uygulama `ESLINT_PATH` değişkenine bakar. ESLint yolunu
tanımlarsanız ESLint çalıştırılır ve kural sonuçları JSON olarak parse edilir.

```
set ESLINT_PATH=C:\tools\node\node_modules\.bin\eslint.cmd
```

Yoksa, uygulama sadece basit `eqeqeq` kontrolü yapan dahili bir tarayıcıya
düşer. ESLint kullandığınızda `no-undef`, `eqeqeq` gibi kuralları
`lint-config.json` ile yönetebilirsiniz.

## Çıktı

Çıktı JSON formatındadır:

```json
[
  {
    "scriptName": "js_generateGraniteRequest",
    "rule": "no-undef",
    "message": "'procAct' is not defined.",
    "line": 12,
    "column": 5,
    "scriptLineOffset": 1234,
    "xmlLine": 1234,
    "xmlPosition": 7
  }
]
```
