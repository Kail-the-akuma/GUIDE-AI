using System;
using System.Collections.Generic;
using System.IO;
using Guide.Core.Interfaces;
using Guide.Semantic;

namespace Guide.Validation
{
    public class LanguageRegistry
    {
        private readonly List<ISemanticParser> _parsers = new();
        private readonly List<ILanguageValidator> _validators = new();
        private readonly List<ICommentStripper> _strippers = new();

        public IEnumerable<ISemanticParser> Parsers
        {
            get
            {
                return _parsers;
            }
        }

        public IEnumerable<ILanguageValidator> Validators
        {
            get
            {
                return _validators;
            }
        }

        public IEnumerable<ICommentStripper> Strippers
        {
            get
            {
                return _strippers;
            }
        }

        public void RegisterParser(ISemanticParser parser)
        {
            _parsers.Add(parser);
        }

        public void RegisterValidator(ILanguageValidator validator)
        {
            _validators.Add(validator);
        }

        public void RegisterStripper(ICommentStripper stripper)
        {
            _strippers.Add(stripper);
        }

        public static LanguageRegistry Detect(string targetPath, ICommandLineRunner runner)
        {
            LanguageRegistry registry = new LanguageRegistry();

            bool hasPackageJson = false;
            bool hasCsproj = false;

            if (Directory.Exists(targetPath))
            {
                SafeScan(targetPath, ref hasPackageJson, ref hasCsproj);
            }
            else if (File.Exists(targetPath))
            {
                string ext = Path.GetExtension(targetPath);
                if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase) || ext.Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    hasCsproj = true;
                }
                else if (Path.GetFileName(targetPath).Equals("package.json", StringComparison.OrdinalIgnoreCase))
                {
                    hasPackageJson = true;
                }
            }

            // Register default strippers
            registry.RegisterStripper(new CSharpCommentStripper());
            registry.RegisterStripper(new TypeScriptCommentStripper());
            registry.RegisterStripper(new HtmlCommentStripper());
            registry.RegisterStripper(new CssCommentStripper());

            if (hasCsproj)
            {
                registry.RegisterValidator(new DotnetValidator(runner));
                registry.RegisterParser(new CSharpParser());
            }

            if (hasPackageJson)
            {
                registry.RegisterValidator(new NodeValidator(runner));
                registry.RegisterParser(new TypeScriptParser());
            }

            return registry;
        }

        private static void SafeScan(string path, ref bool hasPackageJson, ref bool hasCsproj)
        {
            if (hasPackageJson && hasCsproj)
            {
                return;
            }

            try
            {
                foreach (string file in Directory.GetFiles(path))
                {
                    string name = Path.GetFileName(file);
                    if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
                    {
                        hasPackageJson = true;
                    }
                    else if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCsproj = true;
                    }
                }

                foreach (string dir in Directory.GetDirectories(path))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals(".guide", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    SafeScan(dir, ref hasPackageJson, ref hasCsproj);
                }
            }
            catch
            {
                // Ignore directories that can't be read
            }
        }
    }
}
