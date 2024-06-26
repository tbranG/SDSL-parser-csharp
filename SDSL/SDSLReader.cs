﻿using SDSL.Exceptions;
using SDSL.Util;

namespace SDSL;

public class SDSLReader
{
    //sdsl source file
    private FileStream? source;
    private Dictionary<string, dynamic> data = new Dictionary<string, dynamic>();

    //Data getter
    public Dictionary<string, dynamic> Data { get => source is not null ? data : throw new InvalidOperationException("ERR: No file loaded"); }

    public bool IsClosed() => source is null;

    public SDSLReader(FileStream source)
    {
        this.source = source;
        try { BuildDataMap(); }
        catch(Exception err) 
        { 
            PrintErr(err.ToString());
            this.source.Close();
            this.source = null;
        }
    }

    public SDSLReader(string filePath)
    {
        try
        {
            FileInfo info = new FileInfo(filePath);
            if (!info.Extension.Equals(".sdsl")) throw new SDSLInvalidFileExtension("ERR: invalid file type");

            source = File.OpenRead(filePath);
            BuildDataMap();
        }
        catch(Exception err)
        {
            PrintErr(err.ToString());
            source?.Close();
            source = null;
        }
    }

    //Closes the fileStream, blocking users from using this class methods
    public void Close() { source?.Close(); source = null; }

    //Loads all file content synchronously
    private void BuildDataMap()
    {
        if (source is null) return;

        // after reading a dollar sign, buildingKey is true, that means we must add every valid character after that
        // to the key string.
        SDSLStates parserState = SDSLStates.BUILDING_KEY;

        string _key = "";
        dynamic? _tempVal = null;

        Stack<string> parentKey = new();
        Stack<Dictionary<string, dynamic>> maps = new();
        Stack<List<dynamic>> lists = new();

        // --final value type--
        // after reading a key value pair, use this to define to wich type the value will be cast
        SDSLDataTypes? valueType = null; 

        maps.Push(data);
        do
        {
            int charCode = source.ReadByte();
            if (charCode == -1) break;
            char _c = (char)charCode;

            SDSLSymbols symb = CheckSymbol(_c);

            switch (symb)
            {
                case SDSLSymbols.DOLLAR_SIGN:
                    {
                        //if _c == $ we are reading a key
                        parserState = SDSLStates.BUILDING_KEY;

                        //if lists is not empty, we are building an array
                        //so create an dictionary and add to the end of list
                        if(lists.Count > 0)
                        {
                            if(lists.Peek().LastOrDefault() == -1)
                            {
                                lists.Peek().Add(new Dictionary<string, dynamic>());
                                maps.Push(lists.Peek().LastOrDefault());
                            }
                        }

                        break;
                    }
                case SDSLSymbols.LETTER:
                    {
                        // in this case, the character is a letter, that means that we can be reading a key
                        // or a string value

                        if (parserState == SDSLStates.BUILDING_KEY)
                            _key += _c;


                        else if (parserState == SDSLStates.DEFINING_DATA_TYPE || 
                            parserState == SDSLStates.BUILDING_ARRAY ||
                            parserState == SDSLStates.SWITCHING_LINE
                        )
                        {
                            valueType = SDSLDataTypes.STRING;
                            parserState = SDSLStates.READING_STRING;

                            _tempVal ??= "";
                            _tempVal += _c;
                        }


                        else if(parserState == SDSLStates.READING_STRING)
                            _tempVal += _c;
                        

                        break;
                    }
                case SDSLSymbols.NUMBER:
                    {
                        if (parserState == SDSLStates.BUILDING_KEY) throw new SDSLInvalidKeyName("ERR: Attribute name cannot contains numbers");


                        else if(
                            parserState == SDSLStates.DEFINING_DATA_TYPE || 
                            parserState == SDSLStates.BUILDING_ARRAY ||
                            parserState == SDSLStates.SWITCHING_LINE
                        )
                        {
                            valueType = SDSLDataTypes.INTEGER;
                            parserState = SDSLStates.READING_NUMBER;

                            _tempVal ??= "";
                            _tempVal += _c;
                        }   

                        else if (parserState == SDSLStates.READING_NUMBER)
                            _tempVal += _c;
                        

                        break;
                    }
                case SDSLSymbols.SPACE:
                    {
                        if (parserState == SDSLStates.BUILDING_KEY)
                            parentKey?.Push(_key);
                        
                        if (parserState == SDSLStates.BUILDING_KEY || parserState == SDSLStates.BUILDING_ARRAY) parserState = SDSLStates.DEFINING_DATA_TYPE;
                        
                        // this if represents array written in a single line. 
                        // in this case, we should already add _tempVal to the list
                        if (parserState == SDSLStates.BUILDING_ARRAY)
                        {
                            //add to list
                            dynamic? castValue = valueType switch
                            {
                                SDSLDataTypes.INTEGER => int.Parse(_tempVal),
                                SDSLDataTypes.FLOAT => float.Parse(_tempVal, System.Globalization.NumberFormatInfo.InvariantInfo),
                                SDSLDataTypes.STRING => _tempVal,
                                _ => null
                            };

                            lists.Peek().Add(castValue);
                            _tempVal = "";
                        }
                        break;
                    }
                case SDSLSymbols.END_LINE:
                    {
                        //there's no value to add to table
                        if (_tempVal is string && _tempVal.Equals("")) break;

                        
                        //_tempVal casting
                        dynamic? castValue = valueType switch
                        {
                            SDSLDataTypes.INTEGER => int.Parse(_tempVal),
                            SDSLDataTypes.FLOAT => float.Parse(_tempVal, System.Globalization.NumberFormatInfo.InvariantInfo),
                            SDSLDataTypes.STRING => _tempVal,
                            _ => null
                        };


                        //array building exclusive case. If there's no key, we are building an array
                        if (_key.Equals(""))
                            lists.Peek().Add(castValue);
                        else 
                            maps.Peek().Add(_key, castValue);

                        //reseting key and values
                        parserState = SDSLStates.SWITCHING_LINE;
                        valueType = null;

                        _key = "";
                        _tempVal = "";

                        break;
                    }
                case SDSLSymbols.PARENTHESES:
                    {
                        // In this case we are creating an object attribute. The key value is mapping another dictionary
                        if (parserState == SDSLStates.BUILDING_KEY) throw new SDSLInvalidKeyName("ERR: Attribute name can only have letters and underlines");

                        if (parserState == SDSLStates.DEFINING_DATA_TYPE)
                        {
                            maps.Peek().Add(_key, new Dictionary<string, dynamic>());
                            maps.Push(maps.Peek()[_key]);
                            
                            parentKey?.Push(_key);

                            parserState = SDSLStates.BUILDING_OBJECT;
                        }
                        else if (parserState == SDSLStates.BUILDING_OBJECT || parserState == SDSLStates.SWITCHING_LINE)
                        {
                            maps.Pop();
                            parentKey?.Pop();
                            parserState = SDSLStates.SWITCHING_LINE;
                        }

                        _key = "";

                        break;
                    }
                case SDSLSymbols.BRACKETS:
                    {
                        // In this case we are building an array. We add an ArrayList to the key in the top of the stack, and keep pushing new elements in the list
                        // the elements can be of multiple types


                        //one line array treatment
                        if (_tempVal is not null && !(_tempVal.Equals("")))
                        {
                            dynamic? castValue = valueType switch
                            {
                                SDSLDataTypes.INTEGER => int.Parse(_tempVal),
                                SDSLDataTypes.FLOAT => float.Parse(_tempVal, System.Globalization.NumberFormatInfo.InvariantInfo),
                                SDSLDataTypes.STRING => _tempVal,
                                _ => null
                            };

                            lists.Peek().Add(castValue);
                            _tempVal = "";
                        }


                        if (parserState == SDSLStates.BUILDING_KEY) throw new SDSLInvalidKeyName("ERR: Attribute name can only have letters and underlines");

                        if (parserState == SDSLStates.DEFINING_DATA_TYPE)
                        {
                            maps.Peek().Add(_key, new List<dynamic>());

                            lists.Push(maps.Peek()[_key]);
                            parentKey?.Push(_key);

                            parserState = SDSLStates.BUILDING_ARRAY;
                        }
                        else if (parserState == SDSLStates.BUILDING_ARRAY || parserState == SDSLStates.SWITCHING_LINE)
                        {
                            lists.Pop();
                            parentKey?.Pop();
                            parserState = SDSLStates.SWITCHING_LINE;
                        }
 
                        _key = "";

                        break;
                    }
                case SDSLSymbols.SEMICOLON:
                    {
                        if (lists.Count > 0)
                        {
                            //_tempVal casting
                            dynamic? castValue = valueType switch
                            {
                                SDSLDataTypes.INTEGER => int.Parse(_tempVal),
                                SDSLDataTypes.FLOAT => float.Parse(_tempVal, System.Globalization.NumberFormatInfo.InvariantInfo),
                                SDSLDataTypes.STRING => _tempVal,
                                _ => null
                            };

                            lists.Peek().Add(castValue);

                            //reseting state and value
                            parserState = SDSLStates.BUILDING_ARRAY;
                            _tempVal = "";
                        }
                        else throw new Exception("ERR: Unexpected error. There's no list to push element into");
                        break;
                    }
                case SDSLSymbols.DOT:
                    {
                        if (parserState == SDSLStates.READING_NUMBER)
                        {
                            valueType = SDSLDataTypes.FLOAT;
                            _tempVal += _c;
                        }

                        break;
                    }
                default: break;
            }

        } while (source.Position <= source.Length);
    }

    //Every single character from the file is checked
    private static SDSLSymbols CheckSymbol(char symbol) => symbol switch
    {
        '$' => SDSLSymbols.DOLLAR_SIGN,
        '[' or ']' => SDSLSymbols.BRACKETS,
        '(' or ')' => SDSLSymbols.PARENTHESES,
        '\n' => SDSLSymbols.END_LINE,
        ',' => SDSLSymbols.SEMICOLON,
        ' ' => SDSLSymbols.SPACE,
        '.' => SDSLSymbols.DOT,
        _ => IntervalChecker(symbol)
    };

    private static SDSLSymbols IntervalChecker(char symbol)
    {
        int asciiCode = symbol;
        if(asciiCode >= 48 && asciiCode <= 57)
        {
            return SDSLSymbols.NUMBER;
        }
        if((asciiCode >= 65 && asciiCode <= 90) || (asciiCode >= 97 && asciiCode <= 122))
        {
            return SDSLSymbols.LETTER;
        }

        return SDSLSymbols.TRASH;
    }
    //Console message display ----------------------------------------------
    private static void PrintErr(string? message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message ?? "ERR: Something went wrong");
        Console.ResetColor();
    }

    private static void PrintWarn(string? message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message ?? "WARNING: Undefined behavior");
        Console.ResetColor();
    }
}
